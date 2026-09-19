using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;
using AutoMapper;

namespace Ambev.DeveloperEvaluation.Application.Sales.Common;

/// <summary>
/// Maps the <see cref="Sale"/> aggregate onto <see cref="SaleResult"/>.
/// </summary>
/// <remarks>
/// Registered once for all the sale use cases rather than per use case, because
/// they all project the same aggregate onto the same shape. Registering the same
/// map twice makes AutoMapper throw at configuration time.
///
/// The mapping is one-way by design. Nothing maps back onto <see cref="Sale"/>:
/// the aggregate has no public setters and must be changed through its own methods,
/// which is what keeps its invariants enforceable. An object mapper writing
/// directly into an aggregate would bypass every rule it exists to guarantee.
/// </remarks>
public class SaleProfile : Profile
{
    /// <summary>
    /// Initializes the sale mappings.
    /// </summary>
    public SaleProfile()
    {
        // The three External Identity value objects share a shape but not property
        // names: customer and branch expose Name, product exposes Title. Each is
        // mapped onto the common Description field.
        CreateMap<CustomerReference, ExternalIdentityResult>()
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Name));

        CreateMap<BranchReference, ExternalIdentityResult>()
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Name));

        CreateMap<ProductReference, ExternalIdentityResult>()
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Title));

        CreateMap<SaleItem, SaleItemResult>();

        CreateMap<Sale, SaleResult>();
    }
}
