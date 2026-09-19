using Ambev.DeveloperEvaluation.Application.Sales.Common;
using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.CancelSaleItem;

/// <summary>
/// Requests that one item be cancelled while the sale itself remains active.
/// </summary>
/// <param name="SaleId">The sale the item belongs to.</param>
/// <param name="SaleItemId">The item to cancel.</param>
/// <remarks>
/// The sale identifier is carried alongside the item identifier so the item is
/// always reached through its aggregate root. Looking the item up on its own would
/// let a caller cancel an item on a sale they did not name, and would skip the
/// sale-level checks - that the sale is not itself cancelled, and that the total is
/// recalculated afterwards.
///
/// Returns the sale so the caller sees the recalculated total immediately.
/// </remarks>
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
