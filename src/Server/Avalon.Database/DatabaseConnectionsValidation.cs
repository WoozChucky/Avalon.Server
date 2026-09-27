using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Database;

/// <summary>The databases a host opens, each one a <see cref="DatabaseConfiguration"/> entry it needs.</summary>
[Flags]
public enum DatabaseConnections
{
    None = 0,
    Auth = 1,
    Characters = 2,
    World = 4,
}

/// <summary>
/// Refuses a <see cref="DatabaseConfiguration"/> in which a database the host opens has no
/// connection string (#543). Which ones are needed depends on the host, so this is registered by
/// each host rather than written as annotations on the shared class: the auth server opens Auth
/// only, the world server all three. Each failure names the setting, e.g.
/// <c>Database:Auth:ConnectionString is required.</c>
/// </summary>
public sealed class DatabaseConnectionsValidation(string configurationSection, DatabaseConnections required)
    : IValidateOptions<DatabaseConfiguration>
{
    public ValidateOptionsResult Validate(string? name, DatabaseConfiguration options)
    {
        List<string> failures = [];
        Check(DatabaseConnections.Auth, nameof(DatabaseConfiguration.Auth), options.Auth);
        Check(DatabaseConnections.Characters, nameof(DatabaseConfiguration.Characters), options.Characters);
        Check(DatabaseConnections.World, nameof(DatabaseConfiguration.World), options.World);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void Check(DatabaseConnections connection, string key, DatabaseConnection? value)
        {
            if (required.HasFlag(connection) && string.IsNullOrWhiteSpace(value?.ConnectionString))
                failures.Add($"{configurationSection}:{key}:{nameof(DatabaseConnection.ConnectionString)} is required.");
        }
    }
}
