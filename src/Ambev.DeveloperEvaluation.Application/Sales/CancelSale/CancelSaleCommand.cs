using Ambev.DeveloperEvaluation.Application.Sales.Common;
using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.CancelSale;

/// <summary>
/// Requests that a whole sale be cancelled.
/// </summary>
/// <param name="Id">The identifier of the sale to cancel.</param>
/// <remarks>
/// Returns the sale rather than nothing, so the caller can see the zeroed total and
/// the cancellation timestamp without a second request.
/// </remarks>
public record CancelSaleCommand(Guid Id) : IRequest<SaleResult>;

/// <summary>
/// Validates a <see cref="CancelSaleCommand"/>.
/// </summary>
public class CancelSaleValidator : AbstractValidator<CancelSaleCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CancelSaleValidator"/> class.
    /// </summary>
    public CancelSaleValidator()
    {
        RuleFor(c => c.Id)
            .NotEqual(Guid.Empty).WithMessage("The sale identifier is required.");
    }
}
