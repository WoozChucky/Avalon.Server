namespace Avalon.Database.Migrator.Configuration;

/// <summary>
/// Represents the configuration for a migrator.
/// </summary>
public class MigratorConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether this property is enabled.
    /// </summary>
    /// <value>
    /// <c>true</c> if enabled; otherwise, <c>false</c>.
    /// </value>
    public bool Enabled { get; set; }
    
    /// <summary>
    /// Gets or sets a value indicating whether the process is in dry run mode.
    /// </summary>
    /// <remarks>
    /// Dry run mode is a simulation mode where the actual process is not executed,
    /// but instead, only the steps and results are logged or displayed for review.
    /// By default, this property is set to false indicating that the process is not in dry run mode.
    /// </remarks>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether new databases should be created in case they do not exist.
    /// </summary>
    /// <value>
    /// <c>true</c> if new databases should be created; otherwise, <c>false</c>.
    /// </value>
    public bool CreateDatabases { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether migrations should be applied.
    /// </summary>
    /// <value>
    /// <c>true</c> if migrations should be applied; otherwise, <c>false</c>.
    /// </value>
    public bool ApplyMigrations { get; set; } = true;
}
