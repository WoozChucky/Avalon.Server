namespace Avalon.Api.Contract;

public sealed class WorldDto
{
    public ushort Id { get; set; }
    public string Name { get; set; } = "";
    public WorldType Type { get; set; }
    public AccountAccessLevel AccessLevelRequired { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string MinVersion { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>A short text about the world; null for none.</summary>
    public string? Lore { get; set; }
    public WorldStatus Status { get; set; }
    /// <summary>Whether the world has a fresh runtime ready heartbeat.</summary>
    public bool Ready { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Places taken (#967): every admitted account, in the world, at character select, or holding its place.</summary>
    public int OnlineCount { get; set; }

    /// <summary>The accounts the world admits; null for no limit (#967).</summary>
    public int? PlayerLimit { get; set; }

    /// <summary>The accounts waiting for a place (#967).</summary>
    public int QueueLength { get; set; }

    /// <summary>Whether this api is configured for the world's databases (Database:Worlds, #523).</summary>
    public bool Configured { get; set; }

    /// <summary>Configured, and its databases migrated at startup; false answers 503 on its /world/{worldId}/ routes.</summary>
    public bool Available { get; set; }
}
