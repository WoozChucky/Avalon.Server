using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Config;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// The game-auth cryptography has its own key since #801 (design D4.3), <c>Application:GameAuth:HostKey</c>, given the
/// value the HS256 signing key had, from which identity derived its keys before: the keys are the same, so the proofs,
/// replay receipts and Steam OpenID states protected before the change are read after it. For this release an unset host
/// key falls back to the signing key; with neither, or with a host key too short to be one, identity refuses to start,
/// naming the host key.
/// </summary>
public sealed class GameAuthHostKeyShould
{
    private const string Key = "game-auth-host-key-game-auth-host-key-0123456789";

    /// <summary>The cryptography identity registers, with <paramref name="setting"/> set to <paramref name="value"/>.</summary>
    private static GameAuthCryptography Crypto(string setting, string? value)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [setting] = value })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentity(ApplicationConfig.Bind(configuration));
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<GameAuthCryptography>();
    }

    [Theory]
    [InlineData(GameAuthConfig.HostKeySetting)]
    [InlineData("Application:Authentication:IssuerSigningKey")]
    public void Derive_the_keys_identity_derived_from_the_signing_key_before(string setting)
    {
        // As identity built it before #801: from the bytes of the HS256 signing key.
        var before = new GameAuthCryptography(Encoding.UTF8.GetBytes(Key));
        GameAuthCryptography now = Crypto(setting, Key);
        string proof = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string binding = before.Binding("redeem", Guid.NewGuid(), GameAuthCryptography.Digest("request"));
        var state = new AuthenticationProperties();
        state.Items["correlation"] = "c-1";

        Assert.Equal(before.ProofDigest(proof), now.ProofDigest(proof));
        Assert.Equal(before.ProviderProofDigest("steam", "ticket"), now.ProviderProofDigest("steam", "ticket"));
        Assert.Equal("receipt", now.UnprotectText(before.ProtectText("receipt", binding), binding));
        Assert.Equal("c-1", new SteamOpenIdStateFormat(now).Unprotect(new SteamOpenIdStateFormat(before).Protect(state))!
            .Items["correlation"]);
    }

    [Theory]
    [InlineData(null, "is not set")]
    [InlineData("too-short", "is 9 bytes")]
    public void Refuse_a_missing_or_unusable_host_key_naming_it(string? hostKey, string why)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Crypto(GameAuthConfig.HostKeySetting, hostKey));

        Assert.Contains($"{GameAuthConfig.HostKeySetting} {why}", refused.Message, StringComparison.Ordinal);
    }
}
