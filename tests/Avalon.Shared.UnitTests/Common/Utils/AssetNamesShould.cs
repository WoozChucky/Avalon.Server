using Avalon.Common.Utils;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Utils;

/// <summary>The game client's rule for a name that becomes one file's name inside a folder; the server holds its data to it.</summary>
public class AssetNamesShould
{
    [Theory]
    [InlineData("Chest_Wood", true)]
    [InlineData("avalon_shrine", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData(".hidden", false)]
    [InlineData("a..b", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a:b", false)]
    [InlineData("a\u0001b", false)]
    public void Tell_a_plain_asset_name(string? name, bool plain) => Assert.Equal(plain, AssetNames.IsPlain(name));

    [Fact]
    public void Hold_a_plain_asset_name_to_128_characters()
    {
        Assert.True(AssetNames.IsPlain(new string('a', 128)));
        Assert.False(AssetNames.IsPlain(new string('a', 129)));
    }
}
