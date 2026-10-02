using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.Characters;

public class Character : IDbEntity<CharacterId>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public CharacterId Id { get; set; }

    [Required]
    public AccountId AccountId { get; set; }

    private string _name = string.Empty;

    /// <summary>
    /// The name everyone sees. A new character's is stored in <see cref="CharacterName.Display" /> form (#757).
    /// Setting it sets <see cref="NameKey" />, so the two never disagree.
    /// </summary>
    [Required]
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            NameKey = CharacterName.Key(value);
        }
    }

    /// <summary>
    /// The name upper-cased (<see cref="CharacterName.Key" />), which every lookup by name uses (#757). Unique in a
    /// world's characters database, and held to <c>upper("Name")</c> by a check constraint. Set only through
    /// <see cref="Name" />.
    /// </summary>
    [Required]
    public string NameKey { get; private set; } = string.Empty;

    [Required]
    public CharacterClass Class { get; set; }

    [Required]
    public CharacterGender Gender { get; set; }

    [Required]
    public ushort Level { get; set; } = 1;

    [Required]
    public ulong Experience { get; set; } = 0;

    /// <summary>The character's gold, stored in copper. Changed only through IWallet.</summary>
    public ulong Money { get; set; }

    /// <summary>Whether this character is flagged for PvP (#164). Hostile to another flagged player outside towns.</summary>
    public bool PvpEnabled { get; set; }

    /// <summary>UTC. When the flag turns off; null when no off timer is running.</summary>
    public DateTime? PvpOffAt { get; set; }

    public float X { get; set; }

    public float Y { get; set; }
    public float Z { get; set; }

    public float Rotation { get; set; } // Around Y axis

    public ushort Map { get; set; }

    public string? InstanceId { get; set; }

    public bool Online { get; set; }

    public ulong TotalTime { get; set; }

    public ulong LevelTime { get; set; }

    public int LogoutTime { get; set; }

    public bool IsLogoutResting { get; set; }

    public float RestBonus { get; set; }

    public int TotalKills { get; set; }

    public int TodayKills { get; set; }

    public int YesterdayKills { get; set; }

    public int ChosenTitle { get; set; }

    public int Health { get; set; }

    public int Power1 { get; set; }

    public int Power2 { get; set; }

    public int Latency { get; set; }

    public int ActionBars { get; set; }

    public int Order { get; set; }

    public DateTime CreationDate { get; set; }

    public ulong DeleteDate { get; set; }

    /// <summary>
    /// A detached copy of every column, for a save snapshot. The live row keeps changing on the tick
    /// thread while the copy is written from another.
    /// </summary>
    public Character Copy() => (Character)MemberwiseClone();
}
