using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using AutoMapper;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.ListSales;

/// <summary>
/// Handles <see cref="ListSalesCommand"/>.
/// </summary>
public class ListSalesHandler : IRequestHandler<ListSalesCommand, ListSalesResult>
{
    private readonly ISaleRepository _saleRepository;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListSalesHandler"/> class.
    /// </summary>
    /// <param name="saleRepository">The sale repository.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    public ListSalesHandler(ISaleRepository saleRepository, IMapper mapper)
    {
        _saleRepository = saleRepository;
        _mapper = mapper;
    }

    /// <summary>
    /// Retrieves a page of sales.
    /// </summary>
    /// <param name="request">The page, ordering and filters requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of sales and the total that matched.</returns>
    public async Task<ListSalesResult> Handle(ListSalesCommand request, CancellationToken cancellationToken)
    {
        var (sales, totalCount) = await _saleRepository.ListAsync(
            request.Page,
            request.Size,
            request.Order,
            request.Filters,
            cancellationToken);

        return new ListSalesResult
        {
            Sales = _mapper.Map<List<SaleResult>>(sales),
            TotalCount = totalCount,
            CurrentPage = request.Page,
            PageSize = request.Size
        };
    }
}
