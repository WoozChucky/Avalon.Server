using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using OperatingSystem = Avalon.Domain.Auth.OperatingSystem;

namespace Avalon.Api.Contract;

public class AccountDto
{
    public AccountId Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public DateTime JoinDate { get; set; }
    public string LastIp { get; set; }
    public bool Locked { get; set; }
    public DateTime? MuteTime { get; set; }
    public string MuteReason { get; set; }
    public bool Online { get; set; }
    public AccountLocale Locale { get; set; }
    public OperatingSystem Os { get; set; }
    public long TotalTime { get; set; }
    public AccountAccessLevel AccessLevel { get; set; }
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

public enum OperatingSystem : ushort
{
    Windows,
    MacOS,
    Linux
}
