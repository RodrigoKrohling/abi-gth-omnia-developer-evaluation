using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.Services;
using FluentValidation;

namespace Ambev.DeveloperEvaluation.Domain.Validation;

/// <summary>
/// Validates a <see cref="Sale"/> as a whole.
/// </summary>
/// <remarks>
/// This is a second line of defence, not the primary one. The aggregate already
/// refuses to reach an invalid state - <see cref="Sale.AddItem"/> rejects a
/// duplicate product, the discount policy rejects an out-of-range quantity - so a
/// <see cref="Sale"/> instance that violates these rules should not be constructible
/// in the first place.
///
/// Its value is in reporting. The aggregate throws on the first problem it meets,
/// which is right for enforcing an invariant but poor for telling a caller
/// everything that is wrong with their request. This validator walks the whole
/// object and collects every failure, so <c>Sale.Validate()</c> can answer with a
/// complete list.
/// </remarks>
public class SaleValidator : AbstractValidator<Sale>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SaleValidator"/> class.
    /// </summary>
    public SaleValidator()
    {
        RuleFor(sale => sale.SaleNumber)
            .NotEmpty().WithMessage("The sale number is required.")
            .MaximumLength(50).WithMessage("The sale number cannot be longer than 50 characters.");

        RuleFor(sale => sale.SaleDate)
            .NotEmpty().WithMessage("The sale date is required.");

        RuleFor(sale => sale.Customer)
            .NotNull().WithMessage("The customer is required.");

        RuleFor(sale => sale.Customer.Id)
            .NotEqual(Guid.Empty).WithMessage("The customer identifier is required.")
            .When(sale => sale.Customer is not null);

        RuleFor(sale => sale.Customer.Name)
            .NotEmpty().WithMessage("The customer name is required.")
            .When(sale => sale.Customer is not null);

        RuleFor(sale => sale.Branch)
            .NotNull().WithMessage("The branch is required.");

        RuleFor(sale => sale.Branch.Id)
            .NotEqual(Guid.Empty).WithMessage("The branch identifier is required.")
            .When(sale => sale.Branch is not null);

        RuleFor(sale => sale.Branch.Name)
            .NotEmpty().WithMessage("The branch name is required.")
            .When(sale => sale.Branch is not null);

        RuleFor(sale => sale.Items)
            .NotEmpty().WithMessage("A sale must have at least one item.");

        // Each item is checked by its own validator, so the failures come back
        // addressed to the item that caused them rather than to the sale.
        RuleForEach(sale => sale.Items).SetValidator(new SaleItemValidator());

        // Restates the aggregate's one-line-per-product invariant so that a sale
        // materialized straight from the database - which bypasses AddItem - is
        // still reported as invalid if the data was ever corrupted.
        RuleFor(sale => sale.Items)
            .Must(items => items.Select(i => i.Product.Id).Distinct().Count() == items.Count)
            .WithMessage("A product may appear only once in a sale.")
            .When(sale => sale.Items.Count > 0);

        RuleFor(sale => sale.TotalAmount)
            .GreaterThanOrEqualTo(0).WithMessage("The sale total cannot be negative.");
    }
}

/// <summary>
/// Validates a single <see cref="SaleItem"/>.
/// </summary>
/// <remarks>
/// The quantity bounds intentionally repeat the constants on
/// <see cref="QuantityTierDiscountPolicy"/> rather than hard-coding 1 and 20, so
/// that changing the policy cannot leave the validator contradicting it.
/// </remarks>
public class SaleItemValidator : AbstractValidator<SaleItem>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SaleItemValidator"/> class.
    /// </summary>
    public SaleItemValidator()
    {
        RuleFor(item => item.Product)
            .NotNull().WithMessage("The product is required.");

        RuleFor(item => item.Product.Id)
            .NotEqual(Guid.Empty).WithMessage("The product identifier is required.")
            .When(item => item.Product is not null);

        RuleFor(item => item.Product.Title)
            .NotEmpty().WithMessage("The product title is required.")
            .When(item => item.Product is not null);

        RuleFor(item => item.Quantity)
            .GreaterThanOrEqualTo(1)
            .WithMessage("The quantity must be at least 1.")
            .LessThanOrEqualTo(QuantityTierDiscountPolicy.MaximumQuantity)
            .WithMessage($"Cannot sell more than {QuantityTierDiscountPolicy.MaximumQuantity} identical items.");

        RuleFor(item => item.UnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("The unit price cannot be negative.");

        RuleFor(item => item.Discount)
            .GreaterThanOrEqualTo(0).WithMessage("The discount cannot be negative.");

        RuleFor(item => item.TotalAmount)
            .GreaterThanOrEqualTo(0).WithMessage("The item total cannot be negative.");
    }
}
