namespace Avalon.Api.Contract;

[Flags]
public enum AccountAccessLevel : ushort
{
    Player = 1,
    GameMaster = 2,
    Admin = 4,
    Console = 8,
    Tournament = 16,
    PTR = 32,
}
