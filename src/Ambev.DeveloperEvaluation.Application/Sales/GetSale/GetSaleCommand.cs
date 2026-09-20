using Ambev.DeveloperEvaluation.Application.Sales.Common;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.GetSale;

/// <summary>
/// Requests a single sale by its identifier.
/// </summary>
/// <param name="Id">The identifier of the sale to retrieve.</param>
public record GetSaleCommand(Guid Id) : IRequest<SaleResult>;
