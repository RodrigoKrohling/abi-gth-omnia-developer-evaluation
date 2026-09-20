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

        var cached = await _cache.GetAsync<SaleResult>(cacheKey, cancellationToken);

        if (cached is not null)
            return cached;

        var sale = await _saleRepository.GetByIdAsync(request.Id, cancellationToken) ?? throw new KeyNotFoundException($"The sale with ID {request.Id} was not found.");

        var result = _mapper.Map<SaleResult>(sale);

        await _cache.SetAsync(cacheKey, result, SaleCacheKeys.TimeToLive, cancellationToken);

        return result;
    }
}
