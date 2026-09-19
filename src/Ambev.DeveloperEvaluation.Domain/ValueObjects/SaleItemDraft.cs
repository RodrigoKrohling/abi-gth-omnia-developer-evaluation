namespace Ambev.DeveloperEvaluation.Domain.ValueObjects;

/// <summary>
/// The data needed to build one sale item, before the sale has decided whether it
/// is acceptable.
/// </summary>
/// <remarks>
/// A parameter object for <see cref="Entities.Sale.Update"/>, which has to receive
/// a whole set of items at once so it can validate and build them all before
/// touching the sale. Passing three parallel collections, or a tuple, would make
/// that call unreadable and easy to get wrong.
///
/// It is a draft rather than an item because nothing has been validated yet: the
/// quantity may be out of range and the product may be a duplicate. Only
/// <see cref="Entities.SaleItem.Create"/> turns one into a real item, and only
/// after the aggregate has checked the rules that span the sale.
/// </remarks>
/// <param name="Product">The product to sell, as an external identity.</param>
/// <param name="Quantity">The number of units requested.</param>
/// <param name="UnitPrice">The price of a single unit.</param>
public sealed record SaleItemDraft(ProductReference Product, int Quantity, decimal UnitPrice);
