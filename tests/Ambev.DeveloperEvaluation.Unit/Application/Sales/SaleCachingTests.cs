using Ambev.DeveloperEvaluation.Application.Sales.CancelSale;
using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Application.Sales.DeleteSale;
using Ambev.DeveloperEvaluation.Application.Sales.GetSale;
using Ambev.DeveloperEvaluation.Application.Sales.UpdateSale;
using Ambev.DeveloperEvaluation.Common.Caching;
using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using Ambev.DeveloperEvaluation.Domain.Services;
using Ambev.DeveloperEvaluation.Unit.Application.Sales.TestData;
using Ambev.DeveloperEvaluation.Unit.Domain.Entities.TestData;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Ambev.DeveloperEvaluation.Unit.Application.Sales;

/// <summary>
/// Contains unit tests for the read-model caching around the sale use cases.
/// </summary>
/// <remarks>
/// Two properties matter and are tested here. A cached sale must be served without
/// touching the database, otherwise the cache buys nothing. And every write path
/// must invalidate, otherwise the API serves a sale that no longer matches the
/// stored one - which is worse than having no cache at all.
/// </remarks>
public class SaleCachingTests
{
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly IDiscountPolicy _discountPolicy = new QuantityTierDiscountPolicy();
    private readonly AutoMapper.IMapper _mapper = SaleCommandTestData.CreateMapper();

    /// <summary>
    /// Stubs the repository to return the given sale, and the cache to miss.
    /// </summary>
    private void GivenStoredAndNotCached(Sale sale)
    {
        _saleRepository.GetByIdAsync(sale.Id, Arg.Any<CancellationToken>()).Returns(sale);

        _cache.GetAsync<SaleResult>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SaleResult?)null);
    }

    // -- Reading --------------------------------------------------------------

    [Fact(DisplayName = "A cache hit should be served without reading the database")]
    public async Task Given_CachedSale_When_Getting_Then_SkipsRepository()
    {
        var id = Guid.NewGuid();
        var cached = new SaleResult { Id = id, SaleNumber = "CACHED-1" };

        _cache.GetAsync<SaleResult>(SaleCacheKeys.ForSale(id), Arg.Any<CancellationToken>())
            .Returns(cached);

        var handler = new GetSaleHandler(_saleRepository, _cache, _mapper);

        var result = await handler.Handle(new GetSaleCommand(id), CancellationToken.None);

        result.SaleNumber.Should().Be("CACHED-1");

        // The point of the cache: the database is not consulted at all.
        await _saleRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "A cache miss should read the database and populate the cache")]
    public async Task Given_NotCached_When_Getting_Then_ReadsAndPopulates()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        GivenStoredAndNotCached(sale);

        var handler = new GetSaleHandler(_saleRepository, _cache, _mapper);

        await handler.Handle(new GetSaleCommand(sale.Id), CancellationToken.None);

        await _saleRepository.Received(1).GetByIdAsync(sale.Id, Arg.Any<CancellationToken>());

        await _cache.Received(1).SetAsync(
            SaleCacheKeys.ForSale(sale.Id),
            Arg.Any<SaleResult>(),
            SaleCacheKeys.TimeToLive,
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "A missing sale should not be cached")]
    public async Task Given_MissingSale_When_Getting_Then_DoesNotCacheTheMiss()
    {
        _saleRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Sale?)null);
        _cache.GetAsync<SaleResult>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SaleResult?)null);

        var handler = new GetSaleHandler(_saleRepository, _cache, _mapper);

        var act = () => handler.Handle(new GetSaleCommand(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<KeyNotFoundException>();

        // Negative caching would let a burst of requests for a bad id fill the cache
        // with entries that say nothing useful.
        await _cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<SaleResult>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    // -- Invalidation ---------------------------------------------------------

    [Fact(DisplayName = "Updating a sale should invalidate its cache entry")]
    public async Task Given_StoredSale_When_Updated_Then_InvalidatesCache()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        GivenStoredAndNotCached(sale);

        var handler = new UpdateSaleHandler(_saleRepository, _discountPolicy, _cache, _mapper);

        await handler.Handle(SaleCommandTestData.GenerateUpdateCommand(sale.Id), CancellationToken.None);

        await _cache.Received(1).RemoveAsync(SaleCacheKeys.ForSale(sale.Id), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Cancelling a sale should invalidate its cache entry")]
    public async Task Given_StoredSale_When_Cancelled_Then_InvalidatesCache()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        GivenStoredAndNotCached(sale);

        var handler = new CancelSaleHandler(_saleRepository, _cache, _mapper);

        await handler.Handle(new CancelSaleCommand(sale.Id), CancellationToken.None);

        await _cache.Received(1).RemoveAsync(SaleCacheKeys.ForSale(sale.Id), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Deleting a sale should invalidate its cache entry")]
    public async Task Given_StoredSale_When_Deleted_Then_InvalidatesCache()
    {
        var id = Guid.NewGuid();
        _saleRepository.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new DeleteSaleHandler(_saleRepository, _cache);

        await handler.Handle(new DeleteSaleCommand(id), CancellationToken.None);

        // The most important invalidation of the four: a stale entry for a deleted
        // sale would make GET answer 200 for something that no longer exists.
        await _cache.Received(1).RemoveAsync(SaleCacheKeys.ForSale(id), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "A failed delete should not invalidate anything")]
    public async Task Given_MissingSale_When_Deleted_Then_DoesNotInvalidate()
    {
        _saleRepository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = new DeleteSaleHandler(_saleRepository, _cache);

        var act = () => handler.Handle(new DeleteSaleCommand(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<KeyNotFoundException>();

        await _cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // -- Degradation ----------------------------------------------------------

    [Fact(DisplayName = "The no-op cache should always miss and never store")]
    public async Task Given_NullCache_When_Used_Then_AlwaysMisses()
    {
        // Registered when caching is switched off, so that no call site has to branch
        // on whether a cache exists.
        var cache = new NullCacheService();

        await cache.SetAsync("k", new SaleResult(), TimeSpan.FromMinutes(5));

        (await cache.GetAsync<SaleResult>("k")).Should().BeNull();
    }

    [Fact(DisplayName = "Keys should be namespaced and stable for a given sale")]
    public void Given_SaleId_When_BuildingKey_Then_KeyIsNamespacedAndStable()
    {
        var id = Guid.NewGuid();

        // Writers and invalidators must agree on the key; a mismatch would not fail
        // anything loudly, it would just leave entries that are never invalidated.
        SaleCacheKeys.ForSale(id).Should().Be(SaleCacheKeys.ForSale(id));
        SaleCacheKeys.ForSale(id).Should().StartWith("sale:");
        SaleCacheKeys.ForSale(id).Should().NotBe(SaleCacheKeys.ForSale(Guid.NewGuid()));
    }
}
