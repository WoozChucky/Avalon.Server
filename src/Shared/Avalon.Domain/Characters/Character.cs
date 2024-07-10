using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.Mathematics;
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
    
    [Required]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public CharacterClass Class { get; set; }
    
    [Required]
    public CharacterGender Gender { get; set; }
    
    [Required]
    public ushort Level { get; set; } = 1;
    
    [Required]
    public ulong Experience { get; set; } = 0;
    
    public float X { get; set; }
    
    public float Y { get; set; }
    public float Z { get; set; }
    
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
}

public class CharacterMovement
{
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }

    public override string ToString()
    {
        return $"(Position: {Position} Velocity: {Velocity})";
    }
}
