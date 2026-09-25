namespace Avalon.Common.Accounts;

/// <summary>
/// Which access levels may do something, as a mask. Mirrors the API's four authorization policies
/// in Avalon.Api/ServiceRegistration.cs one for one, and AccessLevelsMatchPoliciesShould holds them
/// in step.
/// </summary>
/// <remarks>
/// AccountAccessLevel is [Flags], and Tournament and PTR are NOT above Admin, so an ordinal
/// "at least GameMaster" comparison would let a Tournament or PTR account through. Test membership.
/// </remarks>
public static class AccessLevels
{
    public const AccountAccessLevel Console    = AccountAccessLevel.Console;
    public const AccountAccessLevel Admin      = AccountAccessLevel.Admin | Console;
    public const AccountAccessLevel GameMaster = AccountAccessLevel.GameMaster | Admin;
    // Tournament and PTR are players with exactly the Player permission set. All they add is
    // access to their own worlds, which World.AccessLevelRequired gates, not this mask (#447).
    public const AccountAccessLevel Player     =
        AccountAccessLevel.Player | AccountAccessLevel.Tournament | AccountAccessLevel.PTR | GameMaster;

    /// <summary>
    /// The mask of accounts that may enter a world whose <c>AccessLevelRequired</c> is
    /// <paramref name="required"/> (#447). A Player world admits every player, Tournament and PTR
    /// included; a Tournament or PTR world admits holders of that flag plus staff; a staff-gated
    /// world admits its staff mask. A world requiring several flags admits the union. A world
    /// requiring nothing admits nobody — it is misconfigured, so it fails closed.
    /// </summary>
    /// <remarks>
    /// Never compare a world's level with <c>&lt;=</c>: every account holding PTR (32) or
    /// Tournament (16) is numerically above Admin (4), so an ordinal check lets them into
    /// staff-only worlds and keeps staff out of PTR ones.
    /// </remarks>
    public static AccountAccessLevel ForWorld(AccountAccessLevel required)
    {
        AccountAccessLevel admitted = 0;

        if ((required & AccountAccessLevel.Player) != 0) admitted |= Player;
        if ((required & AccountAccessLevel.Tournament) != 0) admitted |= AccountAccessLevel.Tournament | GameMaster;
        if ((required & AccountAccessLevel.PTR) != 0) admitted |= AccountAccessLevel.PTR | GameMaster;
        if ((required & AccountAccessLevel.GameMaster) != 0) admitted |= GameMaster;
        if ((required & AccountAccessLevel.Admin) != 0) admitted |= Admin;
        if ((required & AccountAccessLevel.Console) != 0) admitted |= Console;

        return admitted;
    }

    /// <summary>True when <paramref name="actual"/> holds any level the mask allows.</summary>
    public static bool Allows(this AccountAccessLevel required, AccountAccessLevel actual)
        => (required & actual) != 0;
}
