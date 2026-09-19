using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Common.Caching;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.DeleteSale;

/// <summary>
/// Handles <see cref="DeleteSaleCommand"/>.
/// </summary>
public class DeleteSaleHandler : IRequestHandler<DeleteSaleCommand, DeleteSaleResult>
{
    private readonly ISaleRepository _saleRepository;
    private readonly ICacheService _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteSaleHandler"/> class.
    /// </summary>
    /// <param name="saleRepository">The sale repository.</param>
    /// <param name="cache">The read-model cache, invalidated after the delete.</param>
    public DeleteSaleHandler(ISaleRepository saleRepository, ICacheService cache)
    {
        _saleRepository = saleRepository;
        _cache = cache;
    }

    /// <summary>
    /// Permanently removes a sale and its items.
    /// </summary>
    /// <param name="request">The sale to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome of the delete.</returns>
    /// <exception cref="ValidationException">Thrown when the identifier is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no sale has that identifier.</exception>
    public async Task<DeleteSaleResult> Handle(DeleteSaleCommand request, CancellationToken cancellationToken)
    {
        var validator = new DeleteSaleValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var deleted = await _saleRepository.DeleteAsync(request.Id, cancellationToken);

        // Deleting something that was never there is reported as a 404 rather than
        // as a success. Answering 200 would tell the caller their request had an
        // effect on a resource that does not exist.
        if (!deleted)
            throw new KeyNotFoundException($"The sale with ID {request.Id} was not found.");

        // Invalidate after the delete. This matters most here: a stale entry for a
        // deleted sale would have GET return 200 with a sale that no longer exists.
        await _cache.RemoveAsync(SaleCacheKeys.ForSale(request.Id), cancellationToken);

        return new DeleteSaleResult(true);
    }
}
