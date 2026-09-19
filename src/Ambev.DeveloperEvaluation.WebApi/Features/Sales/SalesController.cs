using Ambev.DeveloperEvaluation.Application.Sales.CancelSale;
using Ambev.DeveloperEvaluation.Application.Sales.CancelSaleItem;
using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Application.Sales.CreateSale;
using Ambev.DeveloperEvaluation.Application.Sales.DeleteSale;
using Ambev.DeveloperEvaluation.Application.Sales.GetSale;
using Ambev.DeveloperEvaluation.Application.Sales.ListSales;
using Ambev.DeveloperEvaluation.Application.Sales.UpdateSale;
using Ambev.DeveloperEvaluation.WebApi.Common;
using Ambev.DeveloperEvaluation.WebApi.Features.Sales.Common;
using Ambev.DeveloperEvaluation.WebApi.Features.Sales.CreateSale;
using Ambev.DeveloperEvaluation.WebApi.Features.Sales.UpdateSale;
using AutoMapper;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Ambev.DeveloperEvaluation.WebApi.Features.Sales;

/// <summary>
/// Manages sales records.
/// </summary>
/// <remarks>
/// <para>
/// Every action does the same four things: validate the request, map it to a
/// command, send the command, and map the result to a response. No business rule
/// appears here. The controller's job is to translate between HTTP and the
/// application layer, and nothing more.
/// </para>
/// <para>
/// <b>Errors are not handled here.</b> There is no try/catch in this file. Handlers
/// throw <c>KeyNotFoundException</c>, <c>DomainException</c> or
/// <c>ValidationException</c>, and <c>ExceptionHandlingMiddleware</c> turns each into
/// the documented <c>{ type, error, detail }</c> body with the right status code.
/// Catching them here would duplicate that mapping in seven places.
/// </para>
/// <para>
/// <b>Cancel is not Delete.</b> <c>DELETE</c> erases the record; the two
/// <c>PATCH .../cancel</c> endpoints void a sale or one of its lines while keeping it
/// for audit. Cancelling is the business operation and is what a real deployment
/// would use.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class SalesController : BaseController
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesController"/> class.
    /// </summary>
    /// <param name="mediator">The mediator that dispatches commands to handlers.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    public SalesController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    /// <summary>
    /// Registers a new sale.
    /// </summary>
    /// <param name="request">The sale to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sale as stored, including the discounts the server calculated.</returns>
    /// <response code="201">The sale was created.</response>
    /// <response code="400">The request was malformed, or a business rule refused it.</response>
    /// <response code="409">A sale with that number already exists.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponseWithData<SaleResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateSale(
        [FromBody] CreateSaleRequest request,
        CancellationToken cancellationToken)
    {
        var validator = new CreateSaleRequestValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
            // Thrown rather than returned. Passing the failure list to
            // ControllerBase.BadRequest(object) serializes it as a raw JSON array of
            // FluentValidation objects, which is neither the documented
            // { type, error, detail } shape nor anything a client could rely on.
            // Throwing routes it through ExceptionHandlingMiddleware, so controller
            // validation and handler validation produce an identical error body.
            throw new ValidationException(validationResult.Errors);

        var command = _mapper.Map<CreateSaleCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);

        // 201 with a Location header pointing at the new sale, so a client can follow
        // it without having to assemble the URL itself.
        return CreatedAtAction(
            nameof(GetSale),
            new { id = result.Id },
            new ApiResponseWithData<SaleResponse>
            {
                Success = true,
                Message = "Sale created successfully",
                Data = _mapper.Map<SaleResponse>(result)
            });
    }

    /// <summary>
    /// Retrieves a page of sales.
    /// </summary>
    /// <param name="pagination">Paging and ordering, bound from <c>_page</c>, <c>_size</c> and <c>_order</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of sales and the totals needed to navigate it.</returns>
    /// <remarks>
    /// Filtering follows <c>.doc/general-api.md</c>. Any field of the sale may be
    /// filtered, dotted paths reach into the external identities, string values
    /// accept <c>*</c> wildcards, and numeric or date fields accept <c>_min</c> and
    /// <c>_max</c> prefixes:
    ///
    /// <code>
    /// GET /api/sales?_page=2&amp;_size=20&amp;_order=saleDate desc
    /// GET /api/sales?branch.name=Downtown&amp;isCancelled=false
    /// GET /api/sales?customer.name=Maria*&amp;_minSaleDate=2024-01-01
    /// GET /api/sales?_minTotalAmount=100&amp;_maxTotalAmount=500
    /// </code>
    ///
    /// An unrecognised field is ignored rather than rejected, so one stray query
    /// parameter does not fail the request.
    /// </remarks>
    /// <response code="200">The page was returned. An empty page is still a 200.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<SaleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSales(
        [FromQuery] PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        var command = new ListSalesCommand
        {
            Page = pagination.Page,
            Size = pagination.Size,
            Order = pagination.Order,
            // Everything else in the query string is a field filter. The reserved
            // paging keys are stripped downstream by QueryableExtensions, which owns
            // the list of them.
            Filters = Request.Query.ToDictionary(
                pair => pair.Key,
                pair => (string?)pair.Value.ToString())
        };

        var result = await _mediator.Send(command, cancellationToken);

        // OkObjectResult directly, rather than any Ok(...) overload.
        //
        // BaseController declares `protected IActionResult Ok<T>(T data)`, which wraps
        // whatever it is given in an ApiResponseWithData. Passing an envelope that is
        // already an ApiResponseWithData therefore wraps it a second time and the
        // client receives {"data":{"data":[...]}}. Qualifying the call as base.Ok does
        // not help either, since `base` is BaseController - the very class that
        // declares the shadowing helper. Constructing the result explicitly is the
        // only form that cannot bind to it.
        return new OkObjectResult(new PaginatedResponse<SaleResponse>
        {
            Success = true,
            Message = "Sales retrieved successfully",
            Data = _mapper.Map<List<SaleResponse>>(result.Sales),
            CurrentPage = result.CurrentPage,
            TotalPages = result.TotalPages,
            TotalCount = result.TotalCount
        });
    }

    /// <summary>
    /// Retrieves a single sale.
    /// </summary>
    /// <param name="id">The sale's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sale, with all of its items.</returns>
    /// <response code="200">The sale was found.</response>
    /// <response code="400">The identifier was not a usable value.</response>
    /// <response code="404">No sale has that identifier.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponseWithData<SaleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSale(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        // No request class or validator for a single route parameter. The route
        // constraint {id:guid} already rejects anything that is not a Guid, and
        // GetSaleValidator rejects Guid.Empty inside the handler, so a wrapper type
        // here would add a file and catch nothing new.
        var result = await _mediator.Send(new GetSaleCommand(id), cancellationToken);

        return new OkObjectResult(new ApiResponseWithData<SaleResponse>
        {
            Success = true,
            Message = "Sale retrieved successfully",
            Data = _mapper.Map<SaleResponse>(result)
        });
    }

    /// <summary>
    /// Replaces an existing sale.
    /// </summary>
    /// <param name="id">The sale's identifier.</param>
    /// <param name="request">The new state of the sale.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated sale, with discounts and totals recalculated.</returns>
    /// <response code="200">The sale was updated.</response>
    /// <response code="400">The request was malformed, or the sale is cancelled.</response>
    /// <response code="404">No sale has that identifier.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponseWithData<SaleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSale(
        [FromRoute] Guid id,
        [FromBody] UpdateSaleRequest request,
        CancellationToken cancellationToken)
    {
        var validator = new UpdateSaleRequestValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
            // Thrown rather than returned. Passing the failure list to
            // ControllerBase.BadRequest(object) serializes it as a raw JSON array of
            // FluentValidation objects, which is neither the documented
            // { type, error, detail } shape nor anything a client could rely on.
            // Throwing routes it through ExceptionHandlingMiddleware, so controller
            // validation and handler validation produce an identical error body.
            throw new ValidationException(validationResult.Errors);

        var command = _mapper.Map<UpdateSaleCommand>(request);

        // The route is the authority on which sale is being updated; the body does
        // not carry an id, so there is no way for the two to disagree.
        command.Id = id;

        var result = await _mediator.Send(command, cancellationToken);

        return new OkObjectResult(new ApiResponseWithData<SaleResponse>
        {
            Success = true,
            Message = "Sale updated successfully",
            Data = _mapper.Map<SaleResponse>(result)
        });
    }

    /// <summary>
    /// Cancels a whole sale, keeping the record.
    /// </summary>
    /// <param name="id">The sale's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cancelled sale, with its total zeroed.</returns>
    /// <remarks>
    /// The sale stays in the database with its items intact and
    /// <c>isCancelled</c> set, because a voided sale still has to be auditable.
    /// Raises the <c>SaleCancelled</c> event.
    /// </remarks>
    /// <response code="200">The sale was cancelled.</response>
    /// <response code="400">The sale was already cancelled.</response>
    /// <response code="404">No sale has that identifier.</response>
    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponseWithData<SaleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelSale(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelSaleCommand(id), cancellationToken);

        return new OkObjectResult(new ApiResponseWithData<SaleResponse>
        {
            Success = true,
            Message = "Sale cancelled successfully",
            Data = _mapper.Map<SaleResponse>(result)
        });
    }

    /// <summary>
    /// Cancels one item, leaving the rest of the sale active.
    /// </summary>
    /// <param name="id">The sale's identifier.</param>
    /// <param name="itemId">The item's identifier, as returned in the sale's items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sale, with its total recalculated without the cancelled line.</returns>
    /// <remarks>
    /// The item is kept and flagged rather than removed, so the sale still records
    /// what was ordered and later withdrawn. Raises the <c>ItemCancelled</c> event.
    /// </remarks>
    /// <response code="200">The item was cancelled.</response>
    /// <response code="400">The sale or the item was already cancelled.</response>
    /// <response code="404">No such sale, or the sale holds no such item.</response>
    [HttpPatch("{id:guid}/items/{itemId:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponseWithData<SaleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelSaleItem(
        [FromRoute] Guid id,
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CancelSaleItemCommand(id, itemId), cancellationToken);

        return new OkObjectResult(new ApiResponseWithData<SaleResponse>
        {
            Success = true,
            Message = "Sale item cancelled successfully",
            Data = _mapper.Map<SaleResponse>(result)
        });
    }

    /// <summary>
    /// Permanently removes a sale.
    /// </summary>
    /// <param name="id">The sale's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Confirmation that the sale was removed.</returns>
    /// <remarks>
    /// Erases the sale and its items outright. Present because the brief asks for a
    /// complete CRUD API; for voiding a sale in practice, prefer
    /// <c>PATCH /api/sales/{id}/cancel</c>, which keeps the record.
    /// </remarks>
    /// <response code="200">The sale was deleted.</response>
    /// <response code="404">No sale has that identifier.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSale(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteSaleCommand(id), cancellationToken);

        return new OkObjectResult(new ApiResponse
        {
            Success = true,
            Message = "Sale deleted successfully"
        });
    }
}
