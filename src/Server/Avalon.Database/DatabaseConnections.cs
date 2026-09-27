namespace Avalon.Database;

/// <summary>The databases a host opens, each one a <see cref="Avalon.Configuration.DatabaseConfiguration"/> entry it needs.</summary>
[Flags]
public enum DatabaseConnections
{
    None = 0,
    Auth = 1,
    Characters = 2,
    World = 4,
}
