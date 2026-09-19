using Ambev.DeveloperEvaluation.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Ambev.DeveloperEvaluation.ORM.Mongo;

/// <summary>
/// Appends every published domain event to a MongoDB collection, as an audit trail.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of the system that uses the document database the evaluation's
/// tech stack calls for, and it is a good fit for the job. Events are heterogeneous -
/// <c>SaleCreated</c> and <c>ItemCancelled</c> carry different fields - append-only,
/// and never joined. Forcing them into a relational table would mean either a wide
/// sparse table or a column of opaque JSON, whereas a document store keeps each
/// event queryable in its own shape.
/// </para>
/// <para>
/// PostgreSQL remains the system of record. This collection is a derived,
/// append-only history: losing it would lose the audit trail, not the sales.
/// </para>
/// <para>
/// <b>Failures never propagate.</b> Events are published after the business
/// transaction has committed, so an unreachable MongoDB must not turn a successful
/// sale into an error for the caller. Every failure is logged and swallowed. That is
/// the correct trade-off for an audit sink and the wrong one for a system of record,
/// which is why the sale itself is never written here.
/// </para>
/// </remarks>
public class MongoDomainEventStore : IDomainEventPublisher
{
    private readonly MongoSettings _settings;
    private readonly ILogger<MongoDomainEventStore> _logger;

    /// <summary>
    /// The collection events are appended to, resolved once on first use.
    /// </summary>
    /// <remarks>
    /// Lazily created because constructing a <see cref="MongoClient"/> opens a
    /// connection pool, and the application must start cleanly even when MongoDB is
    /// switched off or unavailable.
    /// </remarks>
    private IMongoCollection<BsonDocument>? _collection;

    /// <summary>
    /// Initializes a new instance of the <see cref="MongoDomainEventStore"/> class.
    /// </summary>
    /// <param name="settings">The MongoDB connection settings.</param>
    /// <param name="logger">Logger used to record store failures.</param>
    public MongoDomainEventStore(
        IOptions<MongoSettings> settings,
        ILogger<MongoDomainEventStore> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
            return;

        try
        {
            var collection = GetCollection();

            var documents = domainEvents.Select(ToDocument).ToList();

            if (documents.Count == 0)
                return;

            // One round trip for the whole batch rather than one per event.
            await collection.InsertManyAsync(documents, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            // Deliberately swallowed. The sale is already committed; failing here
            // would report a successful business operation as an error because an
            // audit sink was down.
            _logger.LogError(
                exception,
                "Failed to write domain events to the MongoDB audit store. " +
                "The business operation was not affected.");
        }
    }

    /// <summary>
    /// Resolves the events collection, creating the client on first use.
    /// </summary>
    private IMongoCollection<BsonDocument> GetCollection()
    {
        if (_collection is not null)
            return _collection;

        var client = new MongoClient(_settings.ConnectionString);
        var database = client.GetDatabase(_settings.Database);

        _collection = database.GetCollection<BsonDocument>(_settings.EventsCollection);

        return _collection;
    }

    /// <summary>
    /// Converts a domain event into the document that is stored.
    /// </summary>
    /// <param name="domainEvent">The event to convert.</param>
    /// <returns>The document to insert.</returns>
    /// <remarks>
    /// The event's own fields are nested under <c>payload</c> rather than spread over
    /// the document root, so that two event types with a same-named field of
    /// different type cannot collide, and so the envelope fields stay easy to index.
    /// <c>eventType</c> is stored explicitly because the payload alone does not say
    /// what kind of event it is.
    /// </remarks>
    private static BsonDocument ToDocument(IDomainEvent domainEvent) => new()
    {
        { "eventType", domainEvent.GetType().Name },
        { "occurredOn", domainEvent.OccurredOn },
        { "recordedOn", DateTime.UtcNow },
        { "payload", domainEvent.ToBsonDocument(domainEvent.GetType()) }
    };
}
