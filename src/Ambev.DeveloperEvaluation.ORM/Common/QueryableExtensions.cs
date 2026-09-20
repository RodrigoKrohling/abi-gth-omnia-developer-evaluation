using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Ambev.DeveloperEvaluation.ORM.Common;

/// <summary>
/// Translates the string-based ordering and filtering conventions from
/// <c>.doc/general-api.md</c> into LINQ expression trees.
/// </summary>
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
    public static IQueryable<T> ApplyOrdering<T>(this IQueryable<T> source, string? order) =>
        source.ApplyOrdering(order, out _);

    /// <summary>
    /// Applies an ordering clause and reports whether any term actually resolved.
    /// </summary>
    /// <typeparam name="T">The element type being ordered.</typeparam>
    /// <param name="source">The query to order.</param>
    /// <param name="order">Comma-separated <c>field [asc|desc]</c> terms.</param>
    /// <param name="wasOrdered">
    /// Set to <c>true</c> when at least one term resolved to a real property, which
    /// means the returned query can safely be cast to <see cref="IOrderedQueryable{T}"/>
    /// to append a tiebreaker with <c>ThenBy</c>.
    /// </param>
    /// <returns>The ordered query.</returns>
    public static IQueryable<T> ApplyOrdering<T>(this IQueryable<T> source, string? order, out bool wasOrdered)
    {
        wasOrdered = false;

        if (string.IsNullOrWhiteSpace(order))
            return source;

        order = order.Trim().Trim('"', '\'');

        var isFirstTerm = true;

        foreach (var term in order.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var fieldName = parts[0];
            var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            var parameter = Expression.Parameter(typeof(T), "x");

            if (!TryResolveMember(parameter, fieldName, out var member))
                continue;

            var selector = Expression.Lambda(member, parameter);

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
            wasOrdered = true;
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
        if (comparison == Comparison.Equal && member.Type == typeof(string) && rawValue.Contains('*'))
            return BuildWildcardPredicate(member, parameter, rawValue);

        if (!TryConvert(rawValue, member.Type, out var typedValue))
            return null;

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

        var method = (startsWithWildcard, endsWithWildcard) switch
        {
            (true, true) => StringContains,
            (true, false) => StringEndsWith,
            _ => StringStartsWith
        };

        Expression body = Expression.Call(loweredMember, method, constant);

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
    private static bool TryConvert(string rawValue, Type targetType, out object? value)
    {
        value = null;

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
            return false;
        }
    }
}
