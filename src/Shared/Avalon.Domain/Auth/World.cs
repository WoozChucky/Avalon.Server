using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common;
using Avalon.Common.Accounts;

namespace Avalon.Domain.Auth;

public class World : IDbEntity<WorldId>
{
    [Required] public string Name { get; set; }

    [Required] public WorldType Type { get; set; } = WorldType.PvE;

    [Required] public AccountAccessLevel AccessLevelRequired { get; set; } = AccountAccessLevel.Player;

    [Required] public string Host { get; set; }

    [Required] public int Port { get; set; }

    [Required] public string MinVersion { get; set; }

    [Required] public string Version { get; set; }

    [Required] public bool MaintenanceEnabled { get; set; }

    [Required] public long MaintenanceRevision { get; set; }

    public DateTime? MaintenanceDeadlineUtc { get; set; }

    /// <summary>The highest limit a world may be given (#967).</summary>
    public const int MaxPlayerLimit = 100_000;

    /// <summary>
    /// How many accounts the world admits (#967), from 1 to <see cref="MaxPlayerLimit"/>; null for no limit. Read on each
    /// join request by identity (the world's backstop has its own, <c>Game:PlayerLimit</c>, set to the same number); written
    /// only by <c>IWorldRepository.SetPlayerLimitAsync</c>, by column.
    /// </summary>
    public int? PlayerLimit { get; set; }

    [Required] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required] public DateTime UpdatedAt { get; set; }

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public WorldId Id { get; set; }
}

public class WorldId : ValueObject<ushort>
{
    public WorldId(ushort value) : base(value)
    {
    }

    public static implicit operator WorldId(ushort value) => new(value);

    public static implicit operator WorldId(string value) => new(ushort.Parse(value));
}

public enum WorldType : ushort
{
    PvE,
    PvP,
    Event
}

public enum WorldStatus : ushort
{
    Offline,
    Online,
    Maintenance,

    /// <summary>Online, with a player limit that places taken have reached (#967). Derived where places taken are known.</summary>
    Full
}
