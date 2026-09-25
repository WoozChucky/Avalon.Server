namespace Avalon.Api.Authentication;

public class AvalonRoles
{
    public const string Console = "Console";
    public const string Admin = "Admin";
    public const string GameMaster = "GameMaster";
    public const string Player = "Player";

    // Players with the exact Player permission set; they differ only in which worlds they may enter.
    // Claim values are the AccountAccessLevel flag names, so these must match them exactly.
    public const string Tournament = "Tournament";
    public const string PTR = "PTR";
}
