using System.Numerics;
using Avalon.Domain.Attributes;

namespace Avalon.Domain.Characters;

public class Character
{
    [Column("Id")]
    public int? Id { get; set; }
    
    [Column("Account")]
    public int Account { get; set; }
    
    [Column("Name")]
    public string Name { get; set; }
    
    
    [Column("Class")]
    public int Class { get; set; }
    
    [Column("Gender")]
    public int Gender { get; set; }
    
    [Column("Level")]
    public int Level { get; set; }
    
    [Column("XP")]
    public int XP { get; set; }
    
    
    [Column("PositionX")]
    public float PositionX { get; set; }
    
    [Column("PositionY")]
    public float PositionY { get; set; }
    
    [Column("Map")]
    public int Map { get; set; }

    [Column("InstanceId")]
    public string? InstanceId { get; set; }

    [Column("online")]
    public bool Online { get; set; }

    [Column("TotalTime")]
    public int TotalTime { get; set; }

    [Column("LevelTime")]
    public int LevelTime { get; set; }

    [Column("LogoutTime")]
    public int LogoutTime { get; set; }

    [Column("IsLogoutResting")]
    public bool IsLogoutResting { get; set; }

    [Column("RestBonus")]
    public float RestBonus { get; set; }

    [Column("TotalKills")]
    public int TotalKills { get; set; }
    
    [Column("TodayKills")]
    public int TodayKills { get; set; }
    
    [Column("YesterdayKills")]
    public int YesterdayKills { get; set; }
    
    [Column("ChosenTitle")]
    public int ChosenTitle { get; set; }
    
    [Column("Health")]
    public int Health { get; set; }
    
    [Column("Power1")]
    public int Power1 { get; set; }
    
    [Column("Power2")]
    public int Power2 { get; set; }
    
    [Column("Latency")]
    public int Latency { get; set; }
    
    [Column("ActionBars")]
    public int ActionBars { get; set; }
    
    [Column("Order")]
    public int Order { get; set; }
    
    [Column("CreationDate")]
    public DateTime CreationDate { get; set; }
    
    [Column("DeleteDate")]
    public int DeleteDate { get; set; }

    // Non database properties
    public bool IsChatting { get; set; }
    public float ElapsedGameTime { get; set; }
    public CharacterMovement Movement { get; set; }
    
}

public class CharacterMovement
{
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }

    public override string ToString()
    {
        return $"Position: X-{Position.X} Y-{Position.Y} Velocity: X-{Velocity.X} Y-{Velocity.Y}";
    }
}
