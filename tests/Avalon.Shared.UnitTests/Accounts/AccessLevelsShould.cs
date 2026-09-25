using Avalon.Common.Accounts;
using Xunit;

namespace Avalon.Shared.UnitTests.Accounts;

public class AccessLevelsShould
{
    [Theory]
    [InlineData(AccountAccessLevel.GameMaster)]
    [InlineData(AccountAccessLevel.Admin)]
    [InlineData(AccountAccessLevel.Console)]
    public void Let_Staff_Pass_The_GameMaster_Mask(AccountAccessLevel actual)
        => Assert.True(AccessLevels.GameMaster.Allows(actual));

    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    public void Refuse_Non_Staff_At_The_GameMaster_Mask(AccountAccessLevel actual)
    {
        // Tournament (16) and PTR (32) are numerically above GameMaster (2) and Admin (4). An
        // ordinal ">= GameMaster" check passes every other case in this file and fails exactly
        // these two, which is why they are named rather than folded into a generic "non-staff" case.
        Assert.False(AccessLevels.GameMaster.Allows(actual));
    }

    /// <summary>
    /// Tournament and PTR are players with the exact Player permission set; the only thing they add
    /// is access to their own worlds (#447). A Tournament- or PTR-only account left out of this mask
    /// was refused every default chat command and every Player-policy endpoint.
    /// </summary>
    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.GameMaster)]
    [InlineData(AccountAccessLevel.Admin)]
    [InlineData(AccountAccessLevel.Console)]
    public void Let_Every_Logged_In_Level_Pass_The_Player_Mask(AccountAccessLevel actual)
        => Assert.True(AccessLevels.Player.Allows(actual));

    /// <summary>
    /// Who may enter a world with a given AccessLevelRequired (#447). Player worlds admit every
    /// player, Tournament and PTR included. A Tournament or PTR world admits holders of that flag
    /// plus staff. Staff-gated worlds admit only the staff mask. The cases named here are the ones
    /// the old ordinal "required &lt;= actual" check got wrong: every account holding PTR (32) or
    /// Tournament (16) passed an Admin (4) world, and staff could not reach a PTR world.
    /// </summary>
    [Theory]
    // Player world: every logged-in level.
    [InlineData(AccountAccessLevel.Player, AccountAccessLevel.Player, true)]
    [InlineData(AccountAccessLevel.Player, AccountAccessLevel.PTR, true)]
    [InlineData(AccountAccessLevel.Player, AccountAccessLevel.Tournament, true)]
    [InlineData(AccountAccessLevel.Player, AccountAccessLevel.Admin, true)]
    // PTR world: the PTR flag, or staff.
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.PTR, true)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.Player | AccountAccessLevel.PTR, true)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.GameMaster, true)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Admin, true)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.Console, true)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.Player, false)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.Tournament, false)]
    // Tournament world: the Tournament flag, or staff.
    [InlineData(AccountAccessLevel.Tournament, AccountAccessLevel.Tournament, true)]
    [InlineData(AccountAccessLevel.Tournament, AccountAccessLevel.GameMaster, true)]
    [InlineData(AccountAccessLevel.Tournament, AccountAccessLevel.PTR, false)]
    [InlineData(AccountAccessLevel.Tournament, AccountAccessLevel.Player, false)]
    // Staff-gated worlds: the staff masks, never PTR or Tournament.
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.Admin, true)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.Console, true)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.GameMaster, false)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.PTR, false)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.Tournament, false)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.Player | AccountAccessLevel.PTR, false)]
    [InlineData(AccountAccessLevel.GameMaster, AccountAccessLevel.GameMaster, true)]
    [InlineData(AccountAccessLevel.GameMaster, AccountAccessLevel.PTR, false)]
    [InlineData(AccountAccessLevel.Console, AccountAccessLevel.Admin, false)]
    public void Admit_Exactly_The_Right_Accounts_To_A_World(
        AccountAccessLevel required, AccountAccessLevel actual, bool admitted)
        => Assert.Equal(admitted, AccessLevels.ForWorld(required).Allows(actual));

    /// <summary>
    /// A world that requires nothing is misconfigured; it must fail closed, not open to everyone.
    /// </summary>
    [Fact]
    public void Admit_Nobody_To_A_World_That_Requires_No_Level()
        => Assert.False(AccessLevels.ForWorld(0).Allows(AccountAccessLevel.Console));

    [Fact]
    public void Limit_The_Admin_Mask_To_Admin_And_Console()
    {
        Assert.True(AccessLevels.Admin.Allows(AccountAccessLevel.Admin));
        Assert.True(AccessLevels.Admin.Allows(AccountAccessLevel.Console));
        Assert.False(AccessLevels.Admin.Allows(AccountAccessLevel.GameMaster));
    }
}
