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
    public const AccountAccessLevel Player     = AccountAccessLevel.Player | GameMaster;

    /// <summary>True when <paramref name="actual"/> holds any level the mask allows.</summary>
    public static bool Allows(this AccountAccessLevel required, AccountAccessLevel actual)
        => (required & actual) != 0;
}
