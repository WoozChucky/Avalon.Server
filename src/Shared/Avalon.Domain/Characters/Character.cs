using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.Auth;

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
    
    public int Map { get; set; }
    
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

    // Non database properties
    [NotMapped]
    public bool IsChatting { get; set; }
    [NotMapped]
    public float ElapsedGameTime { get; set; }
    [NotMapped]
    public CharacterMovement Movement { get; set; }
    [NotMapped]
    public DateTime EnteredWorld { get; set; }
}

public class CharacterId : ValueObject<ulong>
{
    public CharacterId(ulong value) : base(value)
    {
    }
    
    public static implicit operator CharacterId(ulong value)
    {
        return new CharacterId(value);
    }
}

public enum CharacterClass : ushort
{
    Warrior = 1,
    Wizard = 2,
    Hunter = 3,
    Healer = 4,
}

public enum CharacterGender : byte
{
    Male = 0,
    Female = 1,
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
