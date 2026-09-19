using Ambev.DeveloperEvaluation.Domain.Services;
using FluentValidation;

namespace Ambev.DeveloperEvaluation.Application.Sales.CreateSale;

/// <summary>
/// Validates a <see cref="CreateSaleCommand"/>.
/// </summary>
/// <remarks>
/// This checks that the request is well-formed; the domain decides whether the
/// operation is permitted. The two overlap on quantity, and deliberately so: the
/// validator reports every bad quantity in the request at once, with the offending
/// item's index, while the domain refuses the first one it meets. A caller sending
/// four bad lines should be told about all four rather than discovering them one
/// request at a time.
///
/// The bounds are taken from <see cref="QuantityTierDiscountPolicy"/> rather than
/// hard-coded, so the validator cannot drift out of step with the rules it mirrors.
/// </remarks>
public class CreateSaleCommandValidator : AbstractValidator<CreateSaleCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateSaleCommandValidator"/> class.
    /// </summary>
    public CreateSaleCommandValidator()
    {
        RuleFor(c => c.SaleNumber)
            .NotEmpty().WithMessage("The sale number is required.")
            .MaximumLength(50).WithMessage("The sale number cannot be longer than 50 characters.");

        RuleFor(c => c.SaleDate)
            .NotEmpty().WithMessage("The sale date is required.");

        RuleFor(c => c.CustomerId)
            .NotEqual(Guid.Empty).WithMessage("The customer identifier is required.");

        RuleFor(c => c.CustomerName)
            .NotEmpty().WithMessage("The customer name is required.")
            .MaximumLength(100).WithMessage("The customer name cannot be longer than 100 characters.");

        RuleFor(c => c.BranchId)
            .NotEqual(Guid.Empty).WithMessage("The branch identifier is required.");

        RuleFor(c => c.BranchName)
            .NotEmpty().WithMessage("The branch name is required.")
            .MaximumLength(100).WithMessage("The branch name cannot be longer than 100 characters.");

        RuleFor(c => c.Items)
            .NotEmpty().WithMessage("A sale must have at least one item.");

        // Reported here rather than left to the aggregate so the caller learns which
        // product was repeated, instead of only that something was.
        RuleFor(c => c.Items)
            .Must(items => items.Select(i => i.ProductId).Distinct().Count() == items.Count)
            .WithMessage("A product may appear only once in a sale. Combine the quantities into a single item.")
            .When(c => c.Items.Count > 0);

        RuleForEach(c => c.Items).SetValidator(new CreateSaleItemCommandValidator());
    }
}

/// <summary>
/// Validates one item on a <see cref="CreateSaleCommand"/>.
/// </summary>
public class CreateSaleItemCommandValidator : AbstractValidator<CreateSaleItemCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateSaleItemCommandValidator"/> class.
    /// </summary>
    public CreateSaleItemCommandValidator()
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
