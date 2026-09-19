namespace Ambev.DeveloperEvaluation.ORM.Mongo;

/// <summary>
/// Connection settings for the MongoDB event store, bound from the <c>Mongo</c>
/// section of configuration.
/// </summary>
/// <remarks>
/// <see cref="Enabled"/> exists so the audit store can be switched off without
/// removing its registration. A reviewer running only the API and PostgreSQL should
/// not have to stand up MongoDB as well, and the tests must not depend on it.
/// </remarks>
public class MongoSettings
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Mongo";

    /// <summary>
    /// Gets or sets a value indicating whether events are written to MongoDB.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>false</c>, so the application runs without MongoDB unless it is
    /// deliberately turned on. Failing closed is the right default for an optional
    /// audit sink.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the MongoDB connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the database that holds the event collection.</summary>
    public string Database { get; set; } = "developer_evaluation";

    /// <summary>Gets or sets the collection that events are appended to.</summary>
    public string EventsCollection { get; set; } = "sale_events";
}
