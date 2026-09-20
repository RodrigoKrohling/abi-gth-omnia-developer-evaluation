using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.DeleteSale;

/// <summary>
/// Requests that a sale be permanently removed.
/// </summary>
/// <param name="Id">The identifier of the sale to delete.</param>
public record DeleteSaleCommand(Guid Id) : IRequest<DeleteSaleResult>;

/// <summary>
/// The outcome of a delete.
/// </summary>
/// <param name="Success">Whether a sale was removed.</param>
public record DeleteSaleResult(bool Success);

/// <summary>
/// Validates a <see cref="DeleteSaleCommand"/>.
/// </summary>
public class DeleteSaleValidator : AbstractValidator<DeleteSaleCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteSaleValidator"/> class.
    /// </summary>
    public DeleteSaleValidator()
    {
        RuleFor(c => c.Id)
            .NotEqual(Guid.Empty).WithMessage("The sale identifier is required.");
    }
}
