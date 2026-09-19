using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.Events;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;
using Ambev.DeveloperEvaluation.ORM;
using Ambev.DeveloperEvaluation.ORM.Events;
using Ambev.DeveloperEvaluation.ORM.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ambev.DeveloperEvaluation.Integration.Events;

/// <summary>
/// Contains integration tests for domain event dispatch from
/// <see cref="DefaultContext.SaveChangesAsync"/>.
/// </summary>
/// <remarks>
/// The guarantee worth testing is the ordering: events are published only after the
/// database commit succeeds, and never when it fails. A subscriber that acts on a
/// change which was rolled back is worse than one that never hears about it.
/// </remarks>
public class DomainEventDispatchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RecordingPublisher _publisher = new();
    private readonly DefaultContext _context;
    private readonly SaleRepository _repository;

    /// <summary>
    /// Sets up a context wired to a publisher that records what it is given.
    /// </summary>
    public DomainEventDispatchTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DefaultContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new DefaultContext(options, _publisher);
        _context.Database.EnsureCreated();

        _repository = new SaleRepository(_context);
    }

    /// <summary>
    /// Builds a valid sale with one item.
    /// </summary>
    private static Sale NewSale(string saleNumber, int quantity = 5, decimal unitPrice = 100m)
    {
        var sale = Sale.Create(
            saleNumber,
            new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new CustomerReference(Guid.NewGuid(), "Maria Silva"),
            new BranchReference(Guid.NewGuid(), "Downtown"));

        sale.AddItem(new ProductReference(Guid.NewGuid(), "Backpack"), quantity, unitPrice);

        return sale;
    }

    [Fact(DisplayName = "Creating a sale should publish SaleCreated")]
    public async Task Given_NewSale_When_Saved_Then_PublishesSaleCreated()
    {
        await _repository.CreateAsync(NewSale("EVT-0001"));

        _publisher.Published.Should().ContainSingle()
            .Which.Should().BeOfType<SaleCreatedEvent>();
    }

    [Fact(DisplayName = "Cancelling a sale should publish SaleCancelled")]
    public async Task Given_StoredSale_When_Cancelled_Then_PublishesSaleCancelled()
    {
        var sale = NewSale("EVT-0002");
        await _repository.CreateAsync(sale);
        _publisher.Published.Clear();

        sale.Cancel();
        await _repository.UpdateAsync(sale);

        var evt = _publisher.Published.Should().ContainSingle()
            .Which.Should().BeOfType<SaleCancelledEvent>().Subject;

        // The event carries the amount being voided, not the zeroed total.
        evt.TotalAmount.Should().Be(450m);
    }

    [Fact(DisplayName = "Cancelling an item should publish ItemCancelled")]
    public async Task Given_StoredSale_When_ItemCancelled_Then_PublishesItemCancelled()
    {
        var sale = NewSale("EVT-0003");
        await _repository.CreateAsync(sale);
        _publisher.Published.Clear();

        sale.CancelItem(sale.Items.Single().Id);
        await _repository.UpdateAsync(sale);

        var evt = _publisher.Published.Should().ContainSingle()
            .Which.Should().BeOfType<ItemCancelledEvent>().Subject;

        evt.SaleTotalAmount.Should().Be(0m);
    }

    [Fact(DisplayName = "Updating a sale should publish exactly one SaleModified")]
    public async Task Given_StoredSale_When_Updated_Then_PublishesOneSaleModified()
    {
        var sale = NewSale("EVT-0004");
        await _repository.CreateAsync(sale);
        _publisher.Published.Clear();

        sale.Update(
            sale.SaleDate,
            new CustomerReference(Guid.NewGuid(), "Joao Souza"),
            new BranchReference(Guid.NewGuid(), "Uptown"),
            [new SaleItemDraft(new ProductReference(Guid.NewGuid(), "Satchel"), 10, 50m)]);

        await _repository.UpdateAsync(sale);

        // One request is one modification, however many fields it touched.
        _publisher.Published.Should().ContainSingle()
            .Which.Should().BeOfType<SaleModifiedEvent>();
    }

    [Fact(DisplayName = "Events should not be published when the save fails")]
    public async Task Given_FailingSave_When_Saved_Then_PublishesNothing()
    {
        // A duplicate sale number violates the unique index, so the insert is rejected
        // by the database after the event has already been raised in memory.
        await _repository.CreateAsync(NewSale("EVT-DUPLICATE"));
        _publisher.Published.Clear();

        var act = () => _repository.CreateAsync(NewSale("EVT-DUPLICATE"));

        await act.Should().ThrowAsync<DbUpdateException>();

        // The whole point of publishing after the commit: a rolled-back operation
        // announces nothing.
        _publisher.Published.Should().BeEmpty();
    }

    [Fact(DisplayName = "Events should be drained so a second save does not republish them")]
    public async Task Given_PublishedEvents_When_SavedAgain_Then_DoesNotRepublish()
    {
        var sale = NewSale("EVT-0005");
        await _repository.CreateAsync(sale);

        _publisher.Published.Should().HaveCount(1);
        _publisher.Published.Clear();

        // Nothing new happened to the aggregate, so saving again must be silent.
        await _repository.UpdateAsync(sale);

        _publisher.Published.Should().BeEmpty();
        sale.DomainEvents.Should().BeEmpty();
    }

    [Fact(DisplayName = "A failing publisher should not fail the business operation")]
    public async Task Given_FailingPublisher_When_Saved_Then_OperationStillSucceeds()
    {
        // The composite isolates each sink, so an unreachable audit store cannot turn
        // a committed sale into an error for the caller.
        var composite = new CompositeDomainEventPublisher(
            [new ThrowingPublisher(), _publisher],
            NullLogger<CompositeDomainEventPublisher>.Instance);

        using var context = new DefaultContext(
            new DbContextOptionsBuilder<DefaultContext>().UseSqlite(_connection).Options,
            composite);

        var repository = new SaleRepository(context);

        var act = () => repository.CreateAsync(NewSale("EVT-0006"));

        await act.Should().NotThrowAsync();

        // And the publisher registered after the failing one still ran.
        _publisher.Published.Should().ContainSingle();
    }

    /// <summary>
    /// Releases the connection, discarding the database.
    /// </summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A publisher that records what it was asked to publish.
    /// </summary>
    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        {
            Published.AddRange(domainEvents);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A publisher that always fails, standing in for an unreachable sink.
    /// </summary>
    private sealed class ThrowingPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The sink is unreachable.");
    }
}
