using System.Numerics;
using Avalon.Domain.Attributes;

namespace Avalon.Domain.Characters;

public class Character
{
    [Column("id")]
    public int Id { get; set; }
    
    [Column("account")]
    public int Account { get; set; }
    
    [Column("name")]
    public string Name { get; set; }
    
    
    [Column("class")]
    public int Class { get; set; }
    
    [Column("gender")]
    public int Gender { get; set; }
    
    [Column("level")]
    public int Level { get; set; }
    
    [Column("xp")]
    public int Xp { get; set; }
    
    
    [Column("position_x")]
    public float PositionX { get; set; }
    
    [Column("position_y")]
    public float PositionY { get; set; }
    
    [Column("map")]
    public int Map { get; set; }

    [Column("instance_id")]
    public string? InstanceId { get; set; }

    [Column("online")]
    public bool Online { get; set; }

    [Column("total_time")]
    public int TotalTime { get; set; }

    [Column("level_time")]
    public int LevelTime { get; set; }

    [Column("logout_time")]
    public int LogoutTime { get; set; }

    [Column("is_logout_resting")]
    public bool IsLogoutResting { get; set; }

    [Column("rest_bonus")]
    public float RestBonus { get; set; }

    [Column("total_kills")]
    public int TotalKills { get; set; }
    
    [Column("today_kills")]
    public int TodayKills { get; set; }
    
    [Column("yesterday_kills")]
    public int YesterdayKills { get; set; }
    
    [Column("chosen_title")]
    public int ChosenTitle { get; set; }
    
    [Column("health")]
    public int Health { get; set; }
    
    [Column("power1")]
    public int Power1 { get; set; }
    
    [Column("power2")]
    public int Power2 { get; set; }
    
    [Column("latency")]
    public int Latency { get; set; }
    
    [Column("action_bars")]
    public int ActionBars { get; set; }
    
    [Column("order")]
    public int Order { get; set; }
    
    [Column("creation_date")]
    public DateTime CreationDate { get; set; }
    
    [Column("delete_date")]
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
