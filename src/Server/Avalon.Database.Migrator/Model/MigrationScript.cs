namespace Avalon.Database.Migrator.Model;

/// <summary>
/// Represents a migration script.
/// </summary>
internal class MigrationScript
{
    /// <summary>
    /// Gets or sets the name of the property.
    /// </summary>
    /// <value>
    /// The name.
    /// </value>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the migration property.
    /// </summary>
    /// <value>
    /// The migration property.
    /// </value>
    public string Migration { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the database property.
    /// </summary>
    /// <remarks>
    /// This property represents the name of the database.
    /// </remarks>
    /// <value>
    /// A string representing the name of the database. If the value is null, the database is not specified.
    /// </value>
    public string? Database { get; set; }

    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    /// <value>
    /// The path.
    /// </value>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content of the property.
    /// </summary>
    /// <value>
    /// The content of the property.
    /// </value>
    public string Content { get; set; } = string.Empty;
}
