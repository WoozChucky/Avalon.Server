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
    public WorldStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int OnlineCount { get; set; }

    /// <summary>Whether this api is configured for the world's databases (Database:Worlds, #523).</summary>
    public bool Configured { get; set; }

    /// <summary>Configured, and its databases migrated at startup; false answers 503 on its /world/{worldId}/ routes.</summary>
    public bool Available { get; set; }
}
