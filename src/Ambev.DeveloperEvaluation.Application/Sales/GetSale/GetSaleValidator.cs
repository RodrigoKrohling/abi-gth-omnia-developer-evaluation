using FluentValidation;

namespace Ambev.DeveloperEvaluation.Application.Sales.GetSale;

/// <summary>
/// Validates a <see cref="GetSaleCommand"/>.
/// </summary>
/// <remarks>
/// Rejecting <see cref="Guid.Empty"/> turns a request that could never match into a
/// 400 naming the problem, instead of a database round trip that ends in a
/// misleading 404.
/// </remarks>
public class GetSaleValidator : AbstractValidator<GetSaleCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetSaleValidator"/> class.
    /// </summary>
    public GetSaleValidator()
    {
        RuleFor(c => c.Id)
            .NotEqual(Guid.Empty).WithMessage("The sale identifier is required.");
    }
}
