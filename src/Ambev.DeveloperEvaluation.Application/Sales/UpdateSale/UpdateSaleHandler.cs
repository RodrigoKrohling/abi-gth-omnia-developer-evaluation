using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using Ambev.DeveloperEvaluation.Domain.Services;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.UpdateSale;

/// <summary>
/// Handles <see cref="UpdateSaleCommand"/>.
/// </summary>
public class UpdateSaleHandler : IRequestHandler<UpdateSaleCommand, SaleResult>
{
    private readonly ISaleRepository _saleRepository;
    private readonly IDiscountPolicy _discountPolicy;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSaleHandler"/> class.
    /// </summary>
    /// <param name="saleRepository">The sale repository.</param>
    /// <param name="discountPolicy">The rules that decide item discounts.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    public UpdateSaleHandler(
        ISaleRepository saleRepository,
        IDiscountPolicy discountPolicy,
        IMapper mapper)
    {
        _saleRepository = saleRepository;
        _discountPolicy = discountPolicy;
        _mapper = mapper;
    }

    /// <summary>
    /// Updates a sale.
    /// </summary>
    /// <param name="command">The new state of the sale.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated sale, with discounts and totals recalculated.</returns>
    /// <exception cref="ValidationException">Thrown when the command is malformed.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no sale has that identifier.</exception>
    /// <exception cref="Domain.Exceptions.DomainException">
    /// Thrown by the aggregate when the sale is cancelled or a draft is not acceptable.
    /// </exception>
    public async Task<SaleResult> Handle(UpdateSaleCommand command, CancellationToken cancellationToken)
    {
        var validator = new UpdateSaleCommandValidator();
        var validationResult = await validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var sale = await _saleRepository.GetByIdAsync(command.Id, cancellationToken);

        if (sale is null)
            throw new KeyNotFoundException($"The sale with ID {command.Id} was not found.");

        // Translated into the domain's parameter object so the aggregate receives one
        // complete picture and can validate the whole set before mutating anything.
        var drafts = command.Items
            .Select(i => new SaleItemDraft(
                new ProductReference(i.ProductId, i.ProductTitle),
                i.Quantity,
                i.UnitPrice))
            .ToList();

        // A single call, so the aggregate reconciles the items, recalculates the
        // total and raises exactly one SaleModifiedEvent for the whole request.
        sale.Update(
            command.SaleDate,
            new CustomerReference(command.CustomerId, command.CustomerName),
            new BranchReference(command.BranchId, command.BranchName),
            drafts,
            _discountPolicy);

        await _saleRepository.UpdateAsync(sale, cancellationToken);

        return _mapper.Map<SaleResult>(sale);
    }
}
