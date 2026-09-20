using Ambev.DeveloperEvaluation.Application.Sales.Common;
using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.CancelSaleItem;

/// <summary>
/// Requests that one item be cancelled while the sale itself remains active.
/// </summary>
/// <param name="SaleId">The sale the item belongs to.</param>
/// <param name="SaleItemId">The item to cancel.</param>
public record CancelSaleItemCommand(Guid SaleId, Guid SaleItemId) : IRequest<SaleResult>;

/// <summary>
/// Validates a <see cref="CancelSaleItemCommand"/>.
/// </summary>
public class CancelSaleItemValidator : AbstractValidator<CancelSaleItemCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CancelSaleItemValidator"/> class.
    /// </summary>
    public CancelSaleItemValidator()
    {
        RuleFor(c => c.SaleId)
            .NotEqual(Guid.Empty).WithMessage("The sale identifier is required.");

        RuleFor(c => c.SaleItemId)
            .NotEqual(Guid.Empty).WithMessage("The sale item identifier is required.");
    }
}
