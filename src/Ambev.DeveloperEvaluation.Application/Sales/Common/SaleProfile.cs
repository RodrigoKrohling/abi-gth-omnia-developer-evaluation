using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;
using AutoMapper;

namespace Ambev.DeveloperEvaluation.Application.Sales.Common;

/// <summary>
/// Maps the <see cref="Sale"/> aggregate onto <see cref="SaleResult"/>.
/// </summary>
public class SaleProfile : Profile
{
    /// <summary>
    /// Initializes the sale mappings.
    /// </summary>
    public SaleProfile()
    {
        CreateMap<CustomerReference, ExternalIdentityResult>()
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Name));

        CreateMap<BranchReference, ExternalIdentityResult>()
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Name));

        CreateMap<ProductReference, ExternalIdentityResult>()
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Title));

        CreateMap<SaleItem, SaleItemResult>();

        CreateMap<Sale, SaleResult>()
            .ForMember(d => d.Items, o => o.MapFrom(s => s.Items.OrderBy(i => i.Product.Title)));
    }
}
