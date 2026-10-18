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

    /// <summary>The limit is in UTF-8 bytes, as the client counts it: an e with an acute accent (U+00E9) takes two.</summary>
    [Theory]
    [InlineData('a', 128, true)]
    [InlineData('a', 129, false)]
    [InlineData('é', 64, true)]
    [InlineData('é', 65, false)]
    public void Hold_a_plain_asset_name_to_128_utf8_bytes(char c, int count, bool plain) =>
        Assert.Equal(plain, AssetNames.IsPlain(new string(c, count)));
}
