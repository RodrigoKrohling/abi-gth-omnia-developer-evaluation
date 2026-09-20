using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Domain.Services;
using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.UpdateSale;

/// <summary>
/// Requests that an existing sale be updated.
/// </summary>
public class UpdateSaleCommand : IRequest<SaleResult>
{
    /// <summary>Gets or sets the identifier of the sale to update.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the date the sale was made.</summary>
    public DateTime SaleDate { get; set; }

    /// <summary>Gets or sets the customer's identifier in the customer domain.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Gets or sets the customer's name at the time of sale.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the branch's identifier in the branch domain.</summary>
    public Guid BranchId { get; set; }

    /// <summary>Gets or sets the branch's name at the time of sale.</summary>
    public string BranchName { get; set; } = string.Empty;

    /// <summary>Gets or sets the complete set of items the sale should hold.</summary>
    public List<UpdateSaleItemCommand> Items { get; set; } = [];
}

/// <summary>
/// One product line on an updated sale.
/// </summary>
public class UpdateSaleItemCommand
{
    /// <summary>Gets or sets the product's identifier in the product domain.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the product's title.</summary>
    public string ProductTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets how many units to sell, between 1 and 20.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the price of one unit.</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Validates an <see cref="UpdateSaleCommand"/>.
/// </summary>
public class UpdateSaleCommandValidator : AbstractValidator<UpdateSaleCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSaleCommandValidator"/> class.
    /// </summary>
    public UpdateSaleCommandValidator()
    {
        RuleFor(c => c.Id)
            .NotEqual(Guid.Empty).WithMessage("The sale identifier is required.");

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

        RuleFor(c => c.Items)
            .Must(items => items.Select(i => i.ProductId).Distinct().Count() == items.Count)
            .WithMessage("A product may appear only once in a sale. Combine the quantities into a single item.")
            .When(c => c.Items.Count > 0);

        RuleForEach(c => c.Items).SetValidator(new UpdateSaleItemCommandValidator());
    }
}

/// <summary>
/// Validates one item on an <see cref="UpdateSaleCommand"/>.
/// </summary>
public class UpdateSaleItemCommandValidator : AbstractValidator<UpdateSaleItemCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSaleItemCommandValidator"/> class.
    /// </summary>
    public UpdateSaleItemCommandValidator()
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
