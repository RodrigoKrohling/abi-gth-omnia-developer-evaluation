using Ambev.DeveloperEvaluation.Application.Sales.Common;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.GetSale;

/// <summary>
/// Requests a single sale by its identifier.
/// </summary>
/// <param name="Id">The identifier of the sale to retrieve.</param>
/// <remarks>
/// A record rather than a class, because a query carrying one value has nothing to
/// gain from mutability and reads better at the call site as
/// <c>new GetSaleCommand(id)</c>.
/// </remarks>
public record GetSaleCommand(Guid Id) : IRequest<SaleResult>;
