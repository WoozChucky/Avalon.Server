using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common;

namespace Avalon.Domain.Auth;

public class World : IDbEntity<WorldId>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public WorldId Id { get; set; }

    [Required]
    public string Name { get; set; }

    [Required]
    public WorldType Type { get; set; } = WorldType.PvE;

    [Required]
    public AccountAccessLevel AccessLevelRequired { get; set; } = AccountAccessLevel.Player;

    [Required]
    public string Host { get; set; }

    [Required]
    public int Port { get; set; }

    [Required]
    public string MinVersion { get; set; }

    [Required]
    public string Version { get; set; }

    [Required]
    public WorldStatus Status { get; set; } = WorldStatus.Offline;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; }
}

public class WorldId : ValueObject<ushort>
{
    public WorldId(ushort value) : base(value) { }
    public static implicit operator WorldId(ushort value)
    {
        return new WorldId(value);
    }
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
    Maintenance
}
