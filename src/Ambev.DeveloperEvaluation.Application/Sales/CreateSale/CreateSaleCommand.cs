using Ambev.DeveloperEvaluation.Application.Sales.Common;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.CreateSale;

/// <summary>
/// Requests that a new sale be registered.
/// </summary>
public class CreateSaleCommand : IRequest<SaleResult>
{
    /// <summary>Gets or sets the business-facing sale number.</summary>
    public string SaleNumber { get; set; } = string.Empty;

    /// <summary>Gets or sets the date the sale was made.</summary>
    public DateTime SaleDate { get; set; }

    /// <summary>Gets or sets the customer's identifier in the customer domain.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Gets or sets the customer's name at the time of sale.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the branch's identifier in the branch domain.</summary>
    public Guid BranchId { get; set; }

    /// <summary>Gets or sets the branch's name at the time of sale.</summary>
    public string BranchName { get; set; } = string.Empty;

    /// <summary>Gets or sets the items to put on the sale.</summary>
    public List<CreateSaleItemCommand> Items { get; set; } = [];
}

/// <summary>
/// One product line requested on a new sale.
/// </summary>
public class CreateSaleItemCommand
{
    /// <summary>Gets or sets the product's identifier in the product domain.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the product's title at the time of sale.</summary>
    public string ProductTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets how many units to sell, between 1 and 20.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the price of one unit.</summary>
    public decimal UnitPrice { get; set; }
}
