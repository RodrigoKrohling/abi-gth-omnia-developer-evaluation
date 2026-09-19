using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.Exceptions;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using Ambev.DeveloperEvaluation.Domain.Services;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.CreateSale;

/// <summary>
/// Handles <see cref="CreateSaleCommand"/>.
/// </summary>
/// <remarks>
/// The handler orchestrates; it does not decide. It validates the request, checks
/// that the sale number is free, asks the domain to build the sale, and persists
/// the result. Every business rule - which quantities are sellable, what discount
/// each earns, whether a product may appear twice - lives in
/// <see cref="Sale"/> and <see cref="IDiscountPolicy"/>, so there is no second place
/// where the pricing rules could quietly diverge.
/// </remarks>
public class CreateSaleHandler : IRequestHandler<CreateSaleCommand, SaleResult>
{
    private readonly ISaleRepository _saleRepository;
    private readonly IDiscountPolicy _discountPolicy;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateSaleHandler"/> class.
    /// </summary>
    /// <param name="saleRepository">The sale repository.</param>
    /// <param name="discountPolicy">The rules that decide item discounts.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    public CreateSaleHandler(
        ISaleRepository saleRepository,
        IDiscountPolicy discountPolicy,
        IMapper mapper)
    {
        _saleRepository = saleRepository;
        _discountPolicy = discountPolicy;
        _mapper = mapper;
    }

    /// <summary>
    /// Registers a new sale.
    /// </summary>
    /// <param name="command">The sale to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sale as stored, including the calculated discounts and totals.</returns>
    /// <exception cref="ValidationException">Thrown when the command is malformed.</exception>
    /// <exception cref="ResourceConflictException">Thrown when the sale number is already taken.</exception>
    public async Task<SaleResult> Handle(CreateSaleCommand command, CancellationToken cancellationToken)
    {
        var validator = new CreateSaleCommandValidator();
        var validationResult = await validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        // Checked before building anything so the caller gets a 409 naming the
        // conflict, rather than a unique index violation surfacing as a 500. The
        // index remains the real guarantee: between this read and the insert, a
        // concurrent request could still take the number, and the database is what
        // settles that race.
        var existing = await _saleRepository.GetBySaleNumberAsync(command.SaleNumber, cancellationToken);

        if (existing is not null)
            throw new ResourceConflictException(
                $"A sale with the number '{command.SaleNumber}' already exists.");

        // The value objects are constructed here, after validation, because they
        // refuse to exist in an invalid state and would otherwise throw
        // ArgumentException where a ValidationException is wanted.
        var sale = Sale.Create(
            command.SaleNumber,
            command.SaleDate,
            new CustomerReference(command.CustomerId, command.CustomerName),
            new BranchReference(command.BranchId, command.BranchName));

        // Each item goes through the aggregate, which applies the discount policy and
        // enforces the quantity limit. The handler never computes a discount itself.
        foreach (var item in command.Items)
        {
            sale.AddItem(
                new ProductReference(item.ProductId, item.ProductTitle),
                item.Quantity,
                item.UnitPrice,
                _discountPolicy);
        }

        var created = await _saleRepository.CreateAsync(sale, cancellationToken);

        return _mapper.Map<SaleResult>(created);
    }
}
