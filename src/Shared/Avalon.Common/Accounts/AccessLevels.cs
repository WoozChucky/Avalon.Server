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

    /// <summary>
    /// The inverse of <see cref="ForWorld"/>: the mask of <c>AccessLevelRequired</c> flags that
    /// admit <paramref name="actual"/>. A world is enterable exactly when
    /// <c>(world.AccessLevelRequired &amp; WorldsEnterableBy(actual)) != 0</c>, which, unlike
    /// <see cref="ForWorld"/>, a database query can evaluate, so it can filter and count a page (#452).
    /// </summary>
    /// <remarks>
    /// Exact because <see cref="ForWorld"/> is a union over the required flags: a world admits an
    /// account when any one of its required flags does.
    /// </remarks>
    public static AccountAccessLevel WorldsEnterableBy(AccountAccessLevel actual)
    {
        AccountAccessLevel enterable = 0;

        foreach (AccountAccessLevel flag in Enum.GetValues(typeof(AccountAccessLevel)))
            if (ForWorld(flag).Allows(actual)) enterable |= flag;

        return enterable;
    }

    /// <summary>True when <paramref name="actual"/> holds any level the mask allows.</summary>
    public static bool Allows(this AccountAccessLevel required, AccountAccessLevel actual)
        => (required & actual) != 0;
}
