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

    [Fact]
    public void Limit_The_Admin_Mask_To_Admin_And_Console()
    {
        Assert.True(AccessLevels.Admin.Allows(AccountAccessLevel.Admin));
        Assert.True(AccessLevels.Admin.Allows(AccountAccessLevel.Console));
        Assert.False(AccessLevels.Admin.Allows(AccountAccessLevel.GameMaster));
    }
}
