using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.CancelSaleItem;

/// <summary>
/// Handles <see cref="CancelSaleItemCommand"/>.
/// </summary>
public class CancelSaleItemHandler : IRequestHandler<CancelSaleItemCommand, SaleResult>
{
    private readonly ISaleRepository _saleRepository;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="CancelSaleItemHandler"/> class.
    /// </summary>
    /// <param name="saleRepository">The sale repository.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    public CancelSaleItemHandler(ISaleRepository saleRepository, IMapper mapper)
    {
        _saleRepository = saleRepository;
        _mapper = mapper;
    }

    /// <summary>
    /// Cancels one item on a sale.
    /// </summary>
    /// <param name="request">The item to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sale, with its total recalculated.</returns>
    /// <exception cref="ValidationException">Thrown when either identifier is empty.</exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no sale has that identifier, or the sale holds no such item.
    /// </exception>
    /// <exception cref="Domain.Exceptions.DomainException">
    /// Thrown by the aggregate when the sale or the item is already cancelled.
    /// </exception>
    public async Task<SaleResult> Handle(CancelSaleItemCommand request, CancellationToken cancellationToken)
    {
        var validator = new CancelSaleItemValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        // Loaded whole, items included, so the aggregate can find the item, refuse if
        // the sale is cancelled, and recalculate the total afterwards.
        var sale = await _saleRepository.GetByIdAsync(request.SaleId, cancellationToken);

        if (sale is null)
            throw new KeyNotFoundException($"The sale with ID {request.SaleId} was not found.");

        // Throws KeyNotFoundException of its own when the sale holds no such item,
        // which the middleware maps to a 404 just like a missing sale.
        sale.CancelItem(request.SaleItemId);

        await _saleRepository.UpdateAsync(sale, cancellationToken);

        return _mapper.Map<SaleResult>(sale);
    }
}
