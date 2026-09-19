using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Ambev.DeveloperEvaluation.ORM.Common;

/// <summary>
/// Translates the string-based ordering and filtering conventions from
/// <c>.doc/general-api.md</c> into LINQ expression trees.
/// </summary>
/// <remarks>
/// Everything here builds an <see cref="Expression"/> rather than filtering an
/// in-memory sequence, so the work is composed into the <see cref="IQueryable{T}"/>
/// and executed by the database as part of a single SQL statement. Materialising
/// the table and filtering it in memory would produce identical results and become
/// unusable at any real size.
///
/// Field names arrive in the JSON casing of the response (<c>saleNumber</c>,
/// <c>customer.name</c>) and are matched case-insensitively against CLR property
/// names, with dots walking into owned value objects.
///
/// Unknown field names are ignored rather than rejected. That keeps an unrecognised
/// query parameter from turning a list request into an error, and it means the
/// reflection lookup can never be used to probe which properties exist.
/// </remarks>
public static class QueryableExtensions
{
    /// <summary>
    /// Query string keys that control pagination rather than filtering, and so must
    /// never be interpreted as field names.
    /// </summary>
    private static readonly HashSet<string> ReservedKeys =
        new(StringComparer.OrdinalIgnoreCase) { "_page", "_size", "_order" };

    /// <summary>
    /// Applies an ordering clause such as <c>"totalAmount desc, saleNumber asc"</c>.
    /// </summary>
    /// <typeparam name="T">The element type being ordered.</typeparam>
    /// <param name="source">The query to order.</param>
    /// <param name="order">
    /// Comma-separated <c>field [asc|desc]</c> terms. The direction is optional and
    /// defaults to ascending. Null, empty or entirely unresolvable input leaves the
    /// query untouched.
    /// </param>
    /// <returns>The ordered query.</returns>
    public static IQueryable<T> ApplyOrdering<T>(this IQueryable<T> source, string? order)
    {
        if (string.IsNullOrWhiteSpace(order))
            return source;

        // A caller may quote the whole clause (_order="price desc, title asc"), in
        // which case the quotes arrive as part of the value.
        order = order.Trim().Trim('"', '\'');

        var isFirstTerm = true;

        foreach (var term in order.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "totalAmount desc" -> field "totalAmount", direction "desc".
            var parts = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var fieldName = parts[0];
            var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            var parameter = Expression.Parameter(typeof(T), "x");

            if (!TryResolveMember(parameter, fieldName, out var member))
                continue;

            // x => x.TotalAmount, boxed to object-returning lambda type via the
            // member's own type so the database sees the correct column type.
            var selector = Expression.Lambda(member, parameter);

            // OrderBy for the first term, ThenBy for the rest; otherwise each term
            // would discard the previous one's ordering.
            var method = (isFirstTerm, descending) switch
            {
                (true, false) => nameof(Queryable.OrderBy),
                (true, true) => nameof(Queryable.OrderByDescending),
                (false, false) => nameof(Queryable.ThenBy),
                (false, true) => nameof(Queryable.ThenByDescending)
            };

            source = source.Provider.CreateQuery<T>(
                Expression.Call(
                    typeof(Queryable),
                    method,
                    [typeof(T), member.Type],
                    source.Expression,
                    Expression.Quote(selector)));

            isFirstTerm = false;
        }

        return source;
    }

    /// <summary>
    /// Applies the field filters carried in a query string.
    /// </summary>
    /// <typeparam name="T">The element type being filtered.</typeparam>
    /// <param name="source">The query to filter.</param>
    /// <param name="filters">
    /// Raw query string pairs. Pagination keys are skipped, and the documented
    /// prefixes are honoured:
    /// <list type="bullet">
    /// <item><c>field=value</c> exact match</item>
    /// <item><c>field=value*</c> starts with, <c>field=*value</c> ends with, <c>field=*value*</c> contains</item>
    /// <item><c>_minField=value</c> greater than or equal</item>
    /// <item><c>_maxField=value</c> less than or equal</item>
    /// </list>
    /// </param>
    /// <returns>The filtered query.</returns>
    public static IQueryable<T> ApplyFilters<T>(
        this IQueryable<T> source,
        IReadOnlyDictionary<string, string?> filters)
    {
        if (filters is null || filters.Count == 0)
            return source;

        foreach (var (rawKey, rawValue) in filters)
        {
            if (string.IsNullOrWhiteSpace(rawKey) || ReservedKeys.Contains(rawKey))
                continue;

            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            var parameter = Expression.Parameter(typeof(T), "x");

            // Strip a range prefix before resolving, since "_minSaleDate" names the
            // property "SaleDate" with a "greater than or equal" comparison.
            var (fieldName, comparison) = ParseKey(rawKey);

            if (!TryResolveMember(parameter, fieldName, out var member))
                continue;

            var predicate = BuildPredicate(member, parameter, rawValue, comparison);
            if (predicate is null)
                continue;

            source = source.Where((Expression<Func<T, bool>>)predicate);
        }

        return source;
    }

    /// <summary>
    /// How a filter value should be compared against a column.
    /// </summary>
    private enum Comparison
    {
        /// <summary>Exact match, or a wildcard string match if the value contains <c>*</c>.</summary>
        Equal,

        /// <summary>Greater than or equal, from a <c>_min</c> prefix.</summary>
        Min,

        /// <summary>Less than or equal, from a <c>_max</c> prefix.</summary>
        Max
    }

    /// <summary>
    /// Splits a query key into the field it names and the comparison it requests.
    /// </summary>
    /// <param name="key">The raw query string key, such as <c>_minSaleDate</c>.</param>
    /// <returns>The bare field name and the comparison to apply.</returns>
    private static (string FieldName, Comparison Comparison) ParseKey(string key)
    {
        if (key.StartsWith("_min", StringComparison.OrdinalIgnoreCase) && key.Length > 4)
            return (key[4..], Comparison.Min);

        if (key.StartsWith("_max", StringComparison.OrdinalIgnoreCase) && key.Length > 4)
            return (key[4..], Comparison.Max);

        return (key, Comparison.Equal);
    }

    /// <summary>
    /// Walks a possibly dotted field path and produces the member access expression.
    /// </summary>
    /// <param name="parameter">The lambda parameter to start from.</param>
    /// <param name="path">A property name, or a dotted path such as <c>customer.name</c>.</param>
    /// <param name="member">The resolved member access expression.</param>
    /// <returns><c>true</c> when every segment resolved; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Matching is case-insensitive so that the JSON casing used in responses
    /// (<c>saleNumber</c>) resolves to the CLR property (<c>SaleNumber</c>). Only
    /// public instance properties are considered, so fields and private state are
    /// not reachable from a query string.
    /// </remarks>
    private static bool TryResolveMember(ParameterExpression parameter, string path, out Expression member)
    {
        member = parameter;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var property = member.Type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));

            if (property is null)
                return false;

            member = Expression.Property(member, property);
        }

        // A path that resolved to nothing but the parameter itself is not a member.
        return member != parameter;
    }

    /// <summary>
    /// Builds the <c>x =&gt; ...</c> predicate for a single filter term.
    /// </summary>
    /// <param name="member">The member access to compare.</param>
    /// <param name="parameter">The lambda parameter the member was built from.</param>
    /// <param name="rawValue">The value as it arrived in the query string.</param>
    /// <param name="comparison">The comparison requested by the key's prefix.</param>
    /// <returns>The predicate lambda, or <c>null</c> when the value cannot be converted.</returns>
    private static LambdaExpression? BuildPredicate(
        Expression member,
        ParameterExpression parameter,
        string rawValue,
        Comparison comparison)
    {
        // Wildcards are a string-only feature and only meaningful for exact-match
        // keys; "_minTitle=A*" has no sensible reading.
        if (comparison == Comparison.Equal && member.Type == typeof(string) && rawValue.Contains('*'))
            return BuildWildcardPredicate(member, parameter, rawValue);

        if (!TryConvert(rawValue, member.Type, out var typedValue))
            return null;

        // Expression.Constant on the unwrapped type keeps the comparison typed; for
        // a nullable column the constant is promoted to match.
        var constant = Expression.Constant(typedValue, member.Type);

        Expression body = comparison switch
        {
            Comparison.Min => Expression.GreaterThanOrEqual(member, constant),
            Comparison.Max => Expression.LessThanOrEqual(member, constant),
            _ => Expression.Equal(member, constant)
        };

        return Expression.Lambda(body, parameter);
    }

    private static readonly MethodInfo StringContains =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    private static readonly MethodInfo StringStartsWith =
        typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;

    private static readonly MethodInfo StringEndsWith =
        typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;

    private static readonly MethodInfo StringToLower =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    /// <summary>
    /// Builds a partial-match predicate for a value containing <c>*</c>.
    /// </summary>
    /// <param name="member">The string member to match.</param>
    /// <param name="parameter">The lambda parameter.</param>
    /// <param name="rawValue">The pattern, such as <c>Fjallraven*</c>.</param>
    /// <returns>The predicate lambda.</returns>
    /// <remarks>
    /// Both sides are lowered so matching is case-insensitive. EF Core translates
    /// <c>ToLower()</c> to SQL <c>LOWER()</c>, which keeps the comparison in the
    /// database; using <see cref="StringComparison"/> overloads instead would fail
    /// to translate and silently fall back to client-side evaluation.
    ///
    /// A null column would throw inside <c>LOWER()</c> in memory, so the predicate
    /// is guarded with a null check that also translates to SQL.
    /// </remarks>
    private static LambdaExpression BuildWildcardPredicate(
        Expression member,
        ParameterExpression parameter,
        string rawValue)
    {
        var startsWithWildcard = rawValue.StartsWith('*');
        var endsWithWildcard = rawValue.EndsWith('*');
        var needle = rawValue.Trim('*').ToLowerInvariant();

        var loweredMember = Expression.Call(member, StringToLower);
        var constant = Expression.Constant(needle, typeof(string));

        // *value*  -> contains
        // *value   -> ends with
        // value*   -> starts with
        var method = (startsWithWildcard, endsWithWildcard) switch
        {
            (true, true) => StringContains,
            (true, false) => StringEndsWith,
            _ => StringStartsWith
        };

        Expression body = Expression.Call(loweredMember, method, constant);

        // Guard against NULL columns before calling LOWER() on them.
        body = Expression.AndAlso(
            Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
            body);

        return Expression.Lambda(body, parameter);
    }

    /// <summary>
    /// Converts a query string value to the CLR type of the property being filtered.
    /// </summary>
    /// <param name="rawValue">The value as text.</param>
    /// <param name="targetType">The property type, possibly nullable.</param>
    /// <param name="value">The converted value.</param>
    /// <returns><c>true</c> when the conversion succeeded; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Returning <c>false</c> rather than throwing means a malformed filter value is
    /// ignored along with its term, instead of failing the whole request. Parsing
    /// uses the invariant culture so that <c>_minTotalAmount=10.5</c> means the same
    /// thing regardless of the server's locale.
    /// </remarks>
    private static bool TryConvert(string rawValue, Type targetType, out object? value)
    {
        value = null;

        // decimal? and DateTime? filter the same way their non-nullable forms do.
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (underlyingType == typeof(string))
            {
                value = rawValue;
            }
            else if (underlyingType == typeof(Guid))
            {
                if (!Guid.TryParse(rawValue, out var guid)) return false;
                value = guid;
            }
            else if (underlyingType.IsEnum)
            {
                // Accepts the enum name, matching how enums are serialised in
                // responses, rather than the underlying integer.
                if (!Enum.TryParse(underlyingType, rawValue, ignoreCase: true, out var parsed)) return false;
                value = parsed;
            }
            else if (underlyingType == typeof(DateTime))
            {
                if (!DateTime.TryParse(rawValue, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
                    return false;
                value = date;
            }
            else if (underlyingType == typeof(bool))
            {
                if (!bool.TryParse(rawValue, out var flag)) return false;
                value = flag;
            }
            else
            {
                value = Convert.ChangeType(rawValue, underlyingType, CultureInfo.InvariantCulture);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            // An unparseable value means the caller sent something like
            // "?_minTotalAmount=abc". The term is dropped and the rest still applies.
            return false;
        }
    }
}
