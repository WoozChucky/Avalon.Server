using Avalon.Domain.Attributes;

namespace Avalon.Domain.Auth;

public class Account
{
    [Column("Id")]
    public int? Id { get; set; }
    
    [Column("Username")]
    public string Username { get; set; }
    
    [Column("Salt")]
    public byte[] Salt { get; set; }
    
    [Column("Verifier")]
    public byte[] Verifier { get; set; }
    
    [Column("SessionKey")]
    public byte[] SessionKey { get; set; }
    
    [Column("totp_secret")]
    public byte[] TotpSecret { get; set; }
    
    [Column("Email")]
    public string Email { get; set; }
    
    [Column("JoinDate")]
    public DateTime JoinDate { get; set; }
    
    [Column("LastIp")]
    public string LastIp { get; set; }
    
    [Column("LastAttemptIp")]
    public string LastAttemptIp { get; set; }
    
    [Column("FailedLogins")]
    public int FailedLogins { get; set; }
    
    [Column("Locked")]
    public bool Locked { get; set; }
    
    [Column("LastLogin")]
    public DateTime LastLogin { get; set; }
    
    [Column("Online")]
    public bool Online { get; set; }
    
    [Column("MuteTime")]
    public DateTime? MuteTime { get; set; }
    
    [Column("MuteReason")]
    public string MuteReason { get; set; }
    
    [Column("MuteBy")]
    public string MuteBy { get; set; }
    
    [Column("Locale")]
    public string Locale { get; set; }
    
    [Column("OS")]
    public string OS { get; set; }
    
    [Column("TotalTime")]
    public long TotalTime { get; set; }

    [Column("Role")]
    public string Role { get; set; }
}
