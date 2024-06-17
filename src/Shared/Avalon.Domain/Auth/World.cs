using Avalon.Domain.Attributes;

namespace Avalon.Domain.Auth;

public class World
{
    [Column("Id")]
    public int? Id { get; set; }
    
    [Column("Name")]
    public string Name { get; set; }
    
    [Column("Type")]
    public WorldType Type { get; set; }
    
    [Column("AccessLevelRequired")]
    public AccountAccessLevel AccessLevelRequired { get; set; }
    
    [Column("Host")]
    public string Host { get; set; }
    
    [Column("Port")]
    public int Port { get; set; }
    
    [Column("MinVersion")]
    public string MinVersion { get; set; }
    
    [Column("Version")]
    public string Version { get; set; }
    
    [Column("Status")]
    public WorldStatus Status { get; set; }
    
    [Column("CreatedAt")] 
    public DateTime CreatedAt { get; set; }
    
    [Column("ExpiresAt")] 
    public DateTime ExpiresAt { get; set; }
}

public enum WorldType : short
{
    PvE,
    PvP,
    Event
}

public enum WorldStatus : short
{
    Offline,
    Online,
    Maintenance
}
