using Ambev.DeveloperEvaluation.Domain.Services;
using FluentValidation;

namespace Ambev.DeveloperEvaluation.WebApi.Features.Sales.CreateSale;

/// <summary>
/// The body of <c>POST /api/sales</c>.
/// </summary>
/// <remarks>
/// Customer, branch and product are supplied as an identifier plus a description,
/// which is the External Identities pattern: the identifier says who they are in
/// their own domain, the description is the copy this sale keeps. The API does not
/// call out to those domains to resolve the descriptions, so a sale can be recorded
/// even when the customer or catalogue service is unavailable, and the recorded text
/// is what was true at the time of sale.
///
/// There is no field for a discount or a total. The server derives both from the
/// quantities, so a client cannot dictate what it is charged.
/// </remarks>
public class CreateSaleRequest
{
    /// <summary>Gets or sets the business-facing sale number. Must be unique.</summary>
    /// <example>SALE-000123</example>
    public string SaleNumber { get; set; } = string.Empty;

    /// <summary>Gets or sets the date the sale was made. Interpreted as UTC when no offset is given.</summary>
    public DateTime SaleDate { get; set; }

    /// <summary>Gets or sets the customer's identifier in the customer domain.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Gets or sets the customer's name, recorded as it is at the time of sale.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the branch's identifier in the branch domain.</summary>
    public Guid BranchId { get; set; }

    /// <summary>Gets or sets the branch's name, recorded as it is at the time of sale.</summary>
    public string BranchName { get; set; } = string.Empty;

    /// <summary>Gets or sets the items to sell. At least one is required.</summary>
    public List<CreateSaleItemRequest> Items { get; set; } = [];
}

/// <summary>
/// One product line on a new sale.
/// </summary>
public class CreateSaleItemRequest
{
    /// <summary>Gets or sets the product's identifier in the product domain.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the product's title, recorded as it is at the time of sale.</summary>
    public string ProductTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets how many units to sell.</summary>
    /// <remarks>
    /// Between 1 and 20. Four or more earns a 10% discount, ten or more earns 20%,
    /// and more than 20 identical items cannot be sold.
    /// </remarks>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the price of a single unit.</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Validates the body of <c>POST /api/sales</c>.
/// </summary>
/// <remarks>
/// Runs in the controller so a malformed request is refused before it becomes a
/// command, which keeps the error message addressed to the field the client
/// actually sent. The application layer validates the command again; that is
/// intentional, since a handler must be safe to call from anywhere, not only from
/// this controller.
/// </remarks>
public class CreateSaleRequestValidator : AbstractValidator<CreateSaleRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateSaleRequestValidator"/> class.
    /// </summary>
    public CreateSaleRequestValidator()
    {
        RuleFor(r => r.SaleNumber)
            .NotEmpty().WithMessage("The sale number is required.")
            .MaximumLength(50).WithMessage("The sale number cannot be longer than 50 characters.");

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

        RuleForEach(r => r.Items).SetValidator(new CreateSaleItemRequestValidator());
    }
}

/// <summary>
/// Validates one item on a create request.
/// </summary>
public class CreateSaleItemRequestValidator : AbstractValidator<CreateSaleItemRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateSaleItemRequestValidator"/> class.
    /// </summary>
    public CreateSaleItemRequestValidator()
    {
        RuleFor(i => i.ProductId)
            .NotEqual(Guid.Empty).WithMessage("The product identifier is required.");

        RuleFor(i => i.ProductTitle)
            .NotEmpty().WithMessage("The product title is required.")
            .MaximumLength(200).WithMessage("The product title cannot be longer than 200 characters.");

        // Bounds come from the policy's constants rather than being hard-coded, so
        // the message and the rule cannot drift from the domain.
        RuleFor(i => i.Quantity)
            .GreaterThanOrEqualTo(1)
            .WithMessage("The quantity must be at least 1.")
            .LessThanOrEqualTo(QuantityTierDiscountPolicy.MaximumQuantity)
            .WithMessage($"Cannot sell more than {QuantityTierDiscountPolicy.MaximumQuantity} identical items.");

        RuleFor(i => i.UnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("The unit price cannot be negative.");
    }
}
