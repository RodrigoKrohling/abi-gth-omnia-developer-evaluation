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
    public string SaleNumber { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the date on which the sale was made.
    /// </summary>
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
    public IReadOnlyCollection<SaleItem> Items => _items.AsReadOnly();

    /// <summary>
    /// Gets the total payable for the sale.
    /// </summary>
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
            SaleDate = NormalizeToUtc(saleDate),
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

        var duplicate = drafts
            .GroupBy(d => d.Product.Id)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
            throw new DomainException(
                $"The product '{duplicate.First().Product.Title}' appears more than once. " +
                "Combine the quantities into a single item.");

        foreach (var draft in drafts)
        {
            policy.GetDiscountRate(draft.Quantity);

            if (draft.UnitPrice < 0)
                throw new DomainException(
                    $"The unit price cannot be negative, but was {draft.UnitPrice}.");

            var cancelledMatch = _items.FirstOrDefault(
                i => i.Product.Id == draft.Product.Id && i.IsCancelled);

            if (cancelledMatch is not null)
                throw new DomainException(
                    $"The item for product '{draft.Product.Title}' was cancelled and cannot be changed.");
        }

        SaleDate = NormalizeToUtc(saleDate);
        Customer = customer;
        Branch = branch;

        foreach (var draft in drafts)
        {
            var existing = _items.FirstOrDefault(i => i.Product.Id == draft.Product.Id && !i.IsCancelled);

            if (existing is null)
            {
                _items.Add(SaleItem.Create(draft.Product, draft.Quantity, draft.UnitPrice, policy));
                continue;
            }

            existing.SetQuantityAndPrice(draft.Quantity, draft.UnitPrice, policy);

            if (!string.Equals(existing.Product.Title, draft.Product.Title, StringComparison.Ordinal))
                existing.SetProductTitle(draft.Product.Title);
        }

        var draftProductIds = drafts.Select(d => d.Product.Id).ToHashSet();

        var removed = _items
            .Where(i => !i.IsCancelled && !draftProductIds.Contains(i.Product.Id))
            .ToList();

        foreach (var item in removed)
            _items.Remove(item);

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
    private void Recalculate() =>
        TotalAmount = _items
            .Where(i => !i.IsCancelled)
            .Sum(i => i.TotalAmount);

    /// <summary>
    /// Converts a caller-supplied sale date to UTC.
    /// </summary>
    /// <param name="value">The date as it was supplied.</param>
    /// <returns>The same instant expressed in UTC.</returns>
    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>
    /// Guards operations that a cancelled sale must refuse.
    /// </summary>
    /// <exception cref="DomainException">Thrown when the sale is cancelled.</exception>
    private void EnsureNotCancelled()
    {
        if (IsCancelled)
            throw new DomainException(
                $"The sale '{SaleNumber}' is cancelled and can no longer be changed.");
    }
}
