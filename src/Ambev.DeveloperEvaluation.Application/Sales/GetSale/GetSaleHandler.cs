using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Common.Caching;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.GetSale;

/// <summary>
/// Handles <see cref="GetSaleCommand"/>.
/// </summary>
/// <remarks>
/// This is the only read path that caches. Fetching a sale by id is the most
/// repeated request in the API and always returns the same document until the sale
/// changes, which is exactly the shape of a good cache entry. The list endpoint is
/// not cached: its result depends on the page, ordering and an open-ended set of
/// filters, so the hit rate would be poor and invalidating it correctly would mean
/// evicting every combination whenever any sale changed.
///
/// What is cached is the <see cref="SaleResult"/> read model, never the aggregate.
/// See <see cref="ICacheService"/> for why.
/// </remarks>
public class GetSaleHandler : IRequestHandler<GetSaleCommand, SaleResult>
{
    private readonly ISaleRepository _saleRepository;
    private readonly ICacheService _cache;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetSaleHandler"/> class.
    /// </summary>
    /// <param name="saleRepository">The sale repository.</param>
    /// <param name="cache">The read-model cache.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    public GetSaleHandler(ISaleRepository saleRepository, ICacheService cache, IMapper mapper)
    {
        _saleRepository = saleRepository;
        _cache = cache;
        _mapper = mapper;
    }

    /// <summary>
    /// Retrieves a sale, from cache when possible.
    /// </summary>
    /// <param name="request">The sale to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sale.</returns>
    /// <exception cref="ValidationException">Thrown when the identifier is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no sale has that identifier.</exception>
    public async Task<SaleResult> Handle(GetSaleCommand request, CancellationToken cancellationToken)
    {
        var validator = new GetSaleValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var cacheKey = SaleCacheKeys.ForSale(request.Id);

        // A cache failure is reported as a miss, so an unreachable Redis costs a
        // database read rather than a failed request.
        var cached = await _cache.GetAsync<SaleResult>(cacheKey, cancellationToken);

        if (cached is not null)
            return cached;

        var sale = await _saleRepository.GetByIdAsync(request.Id, cancellationToken);

        // KeyNotFoundException is the layer's way of saying "no such thing"; the
        // middleware turns it into a 404. The handler stays free of HTTP.
        if (sale is null)
            throw new KeyNotFoundException($"The sale with ID {request.Id} was not found.");

        var result = _mapper.Map<SaleResult>(sale);

        // A miss is not cached. Storing "this sale does not exist" would let a burst
        // of requests for a bad id populate the cache with negative entries, and the
        // 404 is cheap to produce anyway.
        await _cache.SetAsync(cacheKey, result, SaleCacheKeys.TimeToLive, cancellationToken);

        return result;
    }
}
