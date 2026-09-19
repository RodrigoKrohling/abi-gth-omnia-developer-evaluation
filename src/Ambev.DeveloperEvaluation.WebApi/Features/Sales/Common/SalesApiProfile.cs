using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Application.Sales.CreateSale;
using Ambev.DeveloperEvaluation.Application.Sales.UpdateSale;
using Ambev.DeveloperEvaluation.WebApi.Features.Sales.CreateSale;
using Ambev.DeveloperEvaluation.WebApi.Features.Sales.UpdateSale;
using AutoMapper;

namespace Ambev.DeveloperEvaluation.WebApi.Features.Sales.Common;

/// <summary>
/// Maps between the sale API contract and the application layer.
/// </summary>
/// <remarks>
/// Two directions, each with a purpose:
///
/// Request to command, on the way in, so the controller hands the handler a shape it
/// owns rather than one bound straight from HTTP.
///
/// Result to response, on the way out, so the published contract stays a separate
/// type from the application's internal result and the two are kept in step by
/// configuration instead of by hand.
///
/// Every map here is between two flat DTOs with matching member names, so AutoMapper
/// needs no explicit member configuration. Where names genuinely differ - the three
/// External Identity value objects - the mapping lives in
/// <see cref="SaleProfile"/> in the application layer, closer to the domain types it
/// knows about.
/// </remarks>
public class SalesApiProfile : Profile
{
    /// <summary>
    /// Initializes the sale API mappings.
    /// </summary>
    public SalesApiProfile()
    {
        // Inbound: HTTP body -> application command.
        CreateMap<CreateSaleRequest, CreateSaleCommand>();
        CreateMap<CreateSaleItemRequest, CreateSaleItemCommand>();

        CreateMap<UpdateSaleRequest, UpdateSaleCommand>()
            // UpdateSaleRequest carries no Id: the route says which sale is being
            // updated, and SalesController assigns command.Id from it immediately
            // after this map runs, so the two can never disagree. Declared as an
            // explicit ignore rather than left implicit, because an unfillable
            // destination member fails AutoMapper's configuration validation.
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        CreateMap<UpdateSaleItemRequest, UpdateSaleItemCommand>();

        // Outbound: application result -> HTTP body.
        CreateMap<SaleResult, SaleResponse>();
        CreateMap<SaleItemResult, SaleItemResponse>();
        CreateMap<ExternalIdentityResult, ExternalIdentityResponse>();
    }
}
