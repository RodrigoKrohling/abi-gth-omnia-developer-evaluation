using Ambev.DeveloperEvaluation.Domain.Services;
using FluentValidation;

namespace Ambev.DeveloperEvaluation.WebApi.Features.Sales.UpdateSale;

/// <summary>
/// The body of <c>PUT /api/sales/{id}</c>.
/// </summary>
/// <remarks>
/// PUT replaces, so this describes the sale in full. The resulting item set is
/// exactly what <see cref="Items"/> lists: a product currently on the sale but
/// absent here is removed, one already present has its quantity and price updated
/// while keeping its item id, and a new one is added.
///
/// The sale number is not part of the body. It identifies the sale to the business
/// and appears on receipts already issued, so an update must not change it. The sale
/// is addressed by the id in the route.
/// </remarks>
public class UpdateSaleRequest
{
    /// <summary>Gets or sets the date the sale was made.</summary>
    public DateTime SaleDate { get; set; }

    /// <summary>Gets or sets the customer's identifier in the customer domain.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Gets or sets the customer's name.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the branch's identifier in the branch domain.</summary>
    public Guid BranchId { get; set; }

    /// <summary>Gets or sets the branch's name.</summary>
    public string BranchName { get; set; } = string.Empty;

    /// <summary>Gets or sets the complete set of items the sale should hold.</summary>
    public List<UpdateSaleItemRequest> Items { get; set; } = [];
}

/// <summary>
/// One product line on an updated sale.
/// </summary>
public class UpdateSaleItemRequest
{
    /// <summary>Gets or sets the product's identifier in the product domain.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the product's title.</summary>
    public string ProductTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets how many units to sell, between 1 and 20.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the price of a single unit.</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Validates the body of <c>PUT /api/sales/{id}</c>.
/// </summary>
public class UpdateSaleRequestValidator : AbstractValidator<UpdateSaleRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSaleRequestValidator"/> class.
    /// </summary>
    public UpdateSaleRequestValidator()
    {
        RuleFor(r => r.SaleDate)
            .NotEmpty().WithMessage("The sale date is required.");

        RuleFor(r => r.CustomerId)
            .NotEqual(Guid.Empty).WithMessage("The customer identifier is required.");

        RuleFor(r => r.CustomerName)
            .NotEmpty().WithMessage("The customer name is required.")
            .MaximumLength(100).WithMessage("The customer name cannot be longer than 100 characters.");

        RuleFor(r => r.BranchId)
            .NotEqual(Guid.Empty).WithMessage("The branch identifier is required.");

        RuleFor(r => r.BranchName)
            .NotEmpty().WithMessage("The branch name is required.")
            .MaximumLength(100).WithMessage("The branch name cannot be longer than 100 characters.");

        RuleFor(r => r.Items)
            .NotEmpty().WithMessage("A sale must have at least one item.");

        RuleFor(r => r.Items)
            .Must(items => items.Select(i => i.ProductId).Distinct().Count() == items.Count)
            .WithMessage("A product may appear only once in a sale. Combine the quantities into a single item.")
            .When(r => r.Items.Count > 0);

        RuleForEach(r => r.Items).SetValidator(new UpdateSaleItemRequestValidator());
    }
}

/// <summary>
/// Validates one item on an update request.
/// </summary>
public class UpdateSaleItemRequestValidator : AbstractValidator<UpdateSaleItemRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSaleItemRequestValidator"/> class.
    /// </summary>
    public UpdateSaleItemRequestValidator()
    {
        RuleFor(i => i.ProductId)
            .NotEqual(Guid.Empty).WithMessage("The product identifier is required.");

        RuleFor(i => i.ProductTitle)
            .NotEmpty().WithMessage("The product title is required.")
            .MaximumLength(200).WithMessage("The product title cannot be longer than 200 characters.");

        RuleFor(i => i.Quantity)
            .GreaterThanOrEqualTo(1)
            .WithMessage("The quantity must be at least 1.")
            .LessThanOrEqualTo(QuantityTierDiscountPolicy.MaximumQuantity)
            .WithMessage($"Cannot sell more than {QuantityTierDiscountPolicy.MaximumQuantity} identical items.");

        RuleFor(i => i.UnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("The unit price cannot be negative.");
    }
}
