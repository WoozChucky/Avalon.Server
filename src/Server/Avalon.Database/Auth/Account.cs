using System;

namespace Avalon.Database.Auth
{
    public class Account
    {
        [Column("id")]
        public int Id { get; set; }
        
        [Column("username")]
        public string Username { get; set; }
        
        [Column("salt")]
        public byte[] Salt { get; set; }
        
        [Column("verifier")]
        public byte[] Verifier { get; set; }
        
        [Column("session_key")]
        public byte[] SessionKey { get; set; }
        
        [Column("totp_secret")]
        public byte[] TotpSecret { get; set; }
        
        [Column("email")]
        public string Email { get; set; }
        
        [Column("join_date")]
        public DateTime JoinDate { get; set; }
        
        [Column("last_ip")]
        public string LastIp { get; set; }
        
        [Column("last_attempt_ip")]
        public string LastAttemptIp { get; set; }
        
        [Column("failed_logins")]
        public int FailedLogins { get; set; }
        
        [Column("locked")]
        public bool Locked { get; set; }
        
        [Column("last_login")]
        public DateTime LastLogin { get; set; }
        
        [Column("online")]
        public bool Online { get; set; }
        
        [Column("mute_time")]
        public long MuteTime { get; set; }
        
        [Column("mute_reason")]
        public string MuteReason { get; set; }
        
        [Column("mute_by")]
        public string MuteBy { get; set; }
        
        [Column("locale")]
        public string Locale { get; set; }
        
        [Column("os")]
        public string Os { get; set; }
        
        [Column("total_time")]
        public long TotalTime { get; set; }
    }
}
