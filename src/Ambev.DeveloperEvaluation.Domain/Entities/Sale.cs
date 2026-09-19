using Ambev.DeveloperEvaluation.Common.Validation;
using Ambev.DeveloperEvaluation.Domain.Common;
using Ambev.DeveloperEvaluation.Domain.Events;
using Ambev.DeveloperEvaluation.Domain.Exceptions;
using Ambev.DeveloperEvaluation.Domain.Services;
using Ambev.DeveloperEvaluation.Domain.Validation;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;

namespace Ambev.DeveloperEvaluation.Domain.Entities;

/// <summary>
/// A sales record: who bought what, where, for how much, and whether it still
/// stands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aggregate boundary.</b> <see cref="Sale"/> is the root of the sale cluster and
/// <see cref="SaleItem"/> is inside it. Items are reached only through the sale,
/// never loaded or saved on their own, which is what allows rules that no single
/// item could enforce: that one product appears at most once, and that the total is
/// always the sum of the lines that still count.
/// </para>
///
/// <para>
/// <b>Encapsulation.</b> Every property has a private setter and the item list is
/// exposed read-only. The only way to change a sale is through the methods below,
/// so there is no path by which a caller can set a quantity of 50, leave the total
/// stale, or cancel an item without the sale noticing. This is the difference
/// between an aggregate and a bag of public properties.
/// </para>
///
/// <para>
/// <b>External identities.</b> Customer, branch and product are references to other
/// bounded contexts, each carrying an identifier plus a denormalized description
/// captured at the time of sale. See <see cref="CustomerReference"/>.
/// </para>
///
/// <para>
/// <b>Cancellation.</b> Both a sale and an individual item are cancelled softly, by
/// flag. A sales record that vanishes on deletion cannot be audited, and the
/// business needs to know that a sale was made and then voided, not merely that no
/// sale exists.
/// </para>
/// </remarks>
public class Sale : AggregateRoot
{
    /// <summary>
    /// Backing store for the items, so that the public view can stay read-only.
    /// </summary>
    private readonly List<SaleItem> _items = [];

    /// <summary>
    /// Gets the business-facing number that identifies this sale to people.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>Id</c>. The identifier is a surrogate key for the system;
    /// the sale number is what appears on a receipt and what a customer quotes on
    /// the phone. It is unique, enforced by an index in the ORM layer.
    /// </remarks>
    public string SaleNumber { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the date on which the sale was made.
    /// </summary>
    /// <remarks>
    /// Supplied by the caller rather than taken from the clock, because a sale may
    /// be recorded after the fact. <see cref="CreatedAt"/> records when the row was
    /// written; this records when the business event happened.
    /// </remarks>
    public DateTime SaleDate { get; private set; }

    /// <summary>
    /// Gets the customer who made the purchase.
    /// </summary>
    public CustomerReference Customer { get; private set; }

    /// <summary>
    /// Gets the branch where the sale was made.
    /// </summary>
    public BranchReference Branch { get; private set; }

    /// <summary>
    /// Gets the items on this sale, including cancelled ones.
    /// </summary>
    /// <remarks>
    /// Read-only by design. Handing out the mutable list would let a caller add an
    /// item without passing the duplicate-product check, or remove one without
    /// recalculating the total.
    /// </remarks>
    public IReadOnlyCollection<SaleItem> Items => _items.AsReadOnly();

    /// <summary>
    /// Gets the total payable for the sale.
    /// </summary>
    /// <remarks>
    /// Stored rather than computed on read. It is the figure that was actually
    /// charged, and recomputing it later would make historical sales change if the
    /// discount rules ever did. It is recalculated on every mutation, so it cannot
    /// drift from the items.
    /// </remarks>
    public decimal TotalAmount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the whole sale has been cancelled.
    /// </summary>
    public bool IsCancelled { get; private set; }

    /// <summary>
    /// Gets the instant at which the sale row was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the instant of the most recent change, if any.
    /// </summary>
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the instant at which the sale was cancelled, if it was.
    /// </summary>
    public DateTime? CancelledAt { get; private set; }

    /// <summary>
    /// Parameterless constructor reserved for Entity Framework Core materialization.
    /// </summary>
    private Sale()
    {
        Customer = null!;
        Branch = null!;
    }

    /// <summary>
    /// Registers a new sale with no items yet.
    /// </summary>
    /// <param name="saleNumber">The business-facing sale number.</param>
    /// <param name="saleDate">The date the sale was made.</param>
    /// <param name="customer">The purchasing customer.</param>
    /// <param name="branch">The branch where the sale was made.</param>
    /// <returns>The new sale.</returns>
    /// <exception cref="DomainException">Thrown when the sale number is blank.</exception>
    /// <remarks>
    /// Raises <see cref="SaleCreatedEvent"/>. The event is recorded now but not
    /// published until the surrounding transaction commits, by which time the
    /// caller will have added the items, so subscribers see a complete sale.
    /// </remarks>
    public static Sale Create(
        string saleNumber,
        DateTime saleDate,
        CustomerReference customer,
        BranchReference branch)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(branch);

        if (string.IsNullOrWhiteSpace(saleNumber))
            throw new DomainException("The sale number is required.");

        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            SaleNumber = saleNumber.Trim(),
            SaleDate = saleDate,
            Customer = customer,
            Branch = branch,
            TotalAmount = 0m,
            IsCancelled = false,
            CreatedAt = DateTime.UtcNow
        };

        sale.RaiseEvent(new SaleCreatedEvent(
            sale.Id, sale.SaleNumber, customer.Id, branch.Id, sale.TotalAmount, sale._items.Count));

        return sale;
    }

    /// <summary>
    /// Adds a product line to the sale.
    /// </summary>
    /// <param name="product">The product being sold.</param>
    /// <param name="quantity">How many units, between 1 and 20.</param>
    /// <param name="unitPrice">The price of one unit.</param>
    /// <param name="discountPolicy">
    /// The rules that decide the discount. Defaults to
    /// <see cref="QuantityTierDiscountPolicy"/> when omitted.
    /// </param>
    /// <returns>The item that was added.</returns>
    /// <exception cref="DomainException">
    /// Thrown when the sale is cancelled, when the product is already on the sale,
    /// or when the policy rejects the quantity.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The policy is passed in rather than resolved from a container, so the domain
    /// project depends on no DI framework and a test can substitute different rules
    /// in one line. The default keeps ordinary call sites short.
    /// </para>
    /// <para>
    /// <b>One line per product.</b> A product already present is refused instead of
    /// being merged. Allowing two lines for the same product would break the rule
    /// that no more than 20 identical items may be sold - 2 lines of 20 is 40 units
    /// - and would let a caller dodge the discount tiers by splitting 10 units into
    /// three lines that each earn nothing. With this invariant, one line always
    /// holds the entire quantity of that product, so "identical items" has exactly
    /// one meaning. Callers wanting more units change the existing item.
    /// </para>
    /// </remarks>
    public SaleItem AddItem(
        ProductReference product,
        int quantity,
        decimal unitPrice,
        IDiscountPolicy? discountPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        EnsureNotCancelled();

        if (_items.Any(i => i.Product.Id == product.Id))
            throw new DomainException(
                $"The product '{product.Title}' is already on this sale. " +
                "Change the quantity of the existing item instead of adding a second line for the same product.");

        var item = SaleItem.Create(product, quantity, unitPrice, discountPolicy ?? QuantityTierDiscountPolicy.Instance);

        _items.Add(item);
        Recalculate();

        return item;
    }

    /// <summary>
    /// Replaces the sale's details and its entire item list in one operation.
    /// </summary>
    /// <param name="saleDate">The new sale date.</param>
    /// <param name="customer">The new customer reference.</param>
    /// <param name="branch">The new branch reference.</param>
    /// <param name="items">The complete new set of items.</param>
    /// <param name="discountPolicy">
    /// The rules that decide the discount. Defaults to
    /// <see cref="QuantityTierDiscountPolicy"/> when omitted.
    /// </param>
    /// <exception cref="DomainException">
    /// Thrown when the sale is cancelled, when <paramref name="items"/> is empty,
    /// when it names the same product twice, or when the policy rejects a quantity.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Models the PUT semantics of the API: the request carries the whole sale, so
    /// the item list is replaced rather than merged. Building the replacement into a
    /// temporary list first means a failure partway through leaves the sale exactly
    /// as it was, instead of half updated.
    /// </para>
    /// <para>
    /// Raises a single <see cref="SaleModifiedEvent"/>, because one request is one
    /// modification however many fields it touched.
    /// </para>
    /// </remarks>
    public void Update(
        DateTime saleDate,
        CustomerReference customer,
        BranchReference branch,
        IEnumerable<SaleItemDraft> items,
        IDiscountPolicy? discountPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(items);

        EnsureNotCancelled();

        var policy = discountPolicy ?? QuantityTierDiscountPolicy.Instance;
        var drafts = items.ToList();

        if (drafts.Count == 0)
            throw new DomainException("A sale must have at least one item.");

        // Catch a duplicated product before building anything, so the error names the
        // real problem rather than surfacing as a confusing second failure.
        var duplicate = drafts
            .GroupBy(d => d.Product.Id)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
            throw new DomainException(
                $"The product '{duplicate.First().Product.Title}' appears more than once. " +
                "Combine the quantities into a single item.");

        // Build into a scratch list first: if any draft is rejected, the sale has not
        // been touched yet and the caller sees the original state.
        var replacements = drafts
            .Select(d => SaleItem.Create(d.Product, d.Quantity, d.UnitPrice, policy))
            .ToList();

        SaleDate = saleDate;
        Customer = customer;
        Branch = branch;

        _items.Clear();
        _items.AddRange(replacements);

        UpdatedAt = DateTime.UtcNow;
        Recalculate();

        RaiseEvent(new SaleModifiedEvent(Id, SaleNumber, TotalAmount, _items.Count));
    }

    /// <summary>
    /// Cancels the whole sale.
    /// </summary>
    /// <exception cref="DomainException">Thrown when the sale is already cancelled.</exception>
    /// <remarks>
    /// The items are left untouched so the record of what was ordered survives, but
    /// the total is zeroed because nothing is owed on a cancelled sale.
    /// Raises <see cref="SaleCancelledEvent"/>, carrying the total as it stood
    /// before being zeroed, since that is the amount being voided.
    /// </remarks>
    public void Cancel()
    {
        EnsureNotCancelled();

        var cancelledAmount = TotalAmount;

        IsCancelled = true;
        CancelledAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        TotalAmount = 0m;

        RaiseEvent(new SaleCancelledEvent(Id, SaleNumber, cancelledAmount));
    }

    /// <summary>
    /// Cancels one item while leaving the rest of the sale active.
    /// </summary>
    /// <param name="saleItemId">The identifier of the item to cancel.</param>
    /// <exception cref="DomainException">Thrown when the sale or the item is already cancelled.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the sale has no such item.</exception>
    /// <remarks>
    /// The sale total is recalculated so the cancelled line stops counting, and
    /// <see cref="ItemCancelledEvent"/> carries the new total so a subscriber does
    /// not have to re-read the sale to learn it.
    /// </remarks>
    public void CancelItem(Guid saleItemId)
    {
        EnsureNotCancelled();

        var item = _items.FirstOrDefault(i => i.Id == saleItemId)
            ?? throw new KeyNotFoundException(
                $"The sale '{SaleNumber}' has no item with ID {saleItemId}.");

        // Throws if the item was already cancelled, so a repeated request is an
        // error rather than a silent no-op that would emit a duplicate event.
        item.Cancel();

        UpdatedAt = DateTime.UtcNow;
        Recalculate();

        RaiseEvent(new ItemCancelledEvent(
            Id, SaleNumber, item.Id, item.Product.Id, item.Quantity, TotalAmount));
    }

    /// <summary>
    /// Checks the sale against <see cref="SaleValidator"/> and reports every problem
    /// found.
    /// </summary>
    /// <returns>
    /// A result carrying whether the sale is valid and, if not, the complete list of
    /// failures.
    /// </returns>
    /// <remarks>
    /// Mirrors <c>User.Validate()</c> so both entities are checked the same way.
    ///
    /// Note the difference from the methods above: they throw
    /// <see cref="DomainException"/> on the first rule they break, which is how an
    /// invariant is enforced. This collects every failure instead, which is how a
    /// caller is told what to fix. Both are needed, and they answer different
    /// questions.
    /// </remarks>
    public ValidationResultDetail Validate()
    {
        var validator = new SaleValidator();
        var result = validator.Validate(this);

        return new ValidationResultDetail
        {
            IsValid = result.IsValid,
            Errors = result.Errors.Select(o => (ValidationErrorDetail)o)
        };
    }

    /// <summary>
    /// Recomputes <see cref="TotalAmount"/> from the items that still count.
    /// </summary>
    /// <remarks>
    /// Cancelled items are excluded, which is the whole reason cancelling an item
    /// changes the sale total. Called after every mutation so the stored total can
    /// never disagree with the lines.
    /// </remarks>
    private void Recalculate() =>
        TotalAmount = _items
            .Where(i => !i.IsCancelled)
            .Sum(i => i.TotalAmount);

    /// <summary>
    /// Guards operations that a cancelled sale must refuse.
    /// </summary>
    /// <exception cref="DomainException">Thrown when the sale is cancelled.</exception>
    /// <remarks>
    /// A cancelled sale is a closed historical record. Permitting edits would let
    /// the books be rewritten after the fact.
    /// </remarks>
    private void EnsureNotCancelled()
    {
        if (IsCancelled)
            throw new DomainException(
                $"The sale '{SaleNumber}' is cancelled and can no longer be changed.");
    }
}
