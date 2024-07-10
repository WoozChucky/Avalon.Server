using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

public class Account : IDbEntity<AccountId>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public AccountId Id { get; set; } = 0;
    
    [Required]
    public required string Username { get; init; }
    
    [Required]
    public required byte[] Salt { get; init; }
    
    [Required]
    public required byte[] Verifier { get; init; }
    
    [Column("SessionKey")]
    public byte[] SessionKey { get; set; } = [];
    
    [Required]
    public required string Email { get; init; }
    
    [Required]
    public required DateTime JoinDate { get; init; } = DateTime.UtcNow;
    
    public string LastIp { get; set; } = string.Empty;
    
    public string LastAttemptIp { get; set; } = string.Empty;
    
    public int FailedLogins { get; set; }

    public bool Locked { get; set; } = false;
    
    public DateTime LastLogin { get; set; }

    public bool Online { get; set; } = false;
    
    public DateTime? MuteTime { get; set; }
    
    public string MuteReason { get; set; } = string.Empty;
    
    public string MuteBy { get; set; } = string.Empty;
    
    public AccountLocale Locale { get; set; } = AccountLocale.enUS;
    
    public OperatingSystem Os { get; set; } = OperatingSystem.Windows;

    public long TotalTime { get; set; } = 0;

    public AccountAccessLevel AccessLevel { get; set; } = AccountAccessLevel.Player;
}



[Flags]
public enum AccountAccessLevel : ushort
{
    Player = 0,
    GameMaster = 1,
    Administrator = 2,
    Tournament = 4,
    PTR = 8,
}

public enum AccountLocale : ushort
{
    enUS,
    enGB,
    deDE,
    esES,
    esMX,
    frFR,
    itIT,
    plPL,
    ptBR,
    ptPT,
    ruRU,
    koKR,
    zhCN,
    zhTW,
    jaJP,
    thTH,
    viVN,
    idID,
    msMY
}

public enum OperatingSystem : ushort
{
    Windows,
    MacOS,
    Linux
}
