using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Hosting.Config;
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
/// replay receipts and Steam OpenID states protected before the change are read after it. Identity refuses a missing or
/// unusable host key, naming it, whatever else is configured: the HS256 key no longer stands in for it. Its startup
/// check builds the key before it serves (ApiStartupValidationShould).
/// </summary>
public sealed class GameAuthHostKeyShould
{
    private const string Key = "game-auth-host-key-game-auth-host-key-0123456789";

    /// <summary>The cryptography identity registers, with the host key and the HS256 key of before #801 as given.</summary>
    private static GameAuthCryptography Crypto(string? hostKey, string? issuerSigningKey = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [GameAuthConfig.HostKeySetting] = hostKey,
                [TokenValidationConfig.IssuerSigningKeySetting] = issuerSigningKey,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentity(ApplicationConfig.Bind(configuration));
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<GameAuthCryptography>();
    }

    [Fact]
    public void Derive_the_keys_identity_derived_from_the_signing_key_before()
    {
        // As identity built it before #801: from the bytes of the HS256 signing key.
        var before = new GameAuthCryptography(Encoding.UTF8.GetBytes(Key));
        GameAuthCryptography now = Crypto(Key);
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

    /// <summary>
    /// Each with the HS256 key of before #801 still configured, which no longer stands in: no host key; one under 32
    /// bytes, counted in UTF-8 (15 two-byte characters are 30 bytes); and one a key file's newline or a stray space
    /// would key with bytes nobody meant.
    /// </summary>
    [Theory]
    [InlineData(null, "is not set")]
    [InlineData("too-short", "is 9 bytes")]
    [InlineData("ééééééééééééééé", "is 30 bytes")]
    [InlineData(Key + "\n", "has leading or trailing whitespace")]
    [InlineData(" " + Key, "has leading or trailing whitespace")]
    public void Refuse_a_missing_or_unusable_host_key_naming_it(string? hostKey, string why)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Crypto(hostKey, Key));

        Assert.Contains($"{GameAuthConfig.HostKeySetting} {why}", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Application__GameAuth__HostKey", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_host_key_that_has_been_public()
    {
        const string MadeUpPublicKey = "made-up-public-key-made-up-public-key-0123456789";
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(MadeUpPublicKey)));

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            GameAuthHostKey.From(new GameAuthConfig { HostKey = MadeUpPublicKey }, [hash]));

        Assert.Contains($"{GameAuthConfig.HostKeySetting} is a key that was committed to the repository", refused.Message,
            StringComparison.Ordinal);
        // Production blocks the HS256 key once committed to appsettings.json: not the value, which is not in the tree,
        // but its hash. An empty list would block nothing.
        Assert.NotEmpty(GameAuthHostKey.BlockedKeyHashes);
        Assert.All(GameAuthHostKey.BlockedKeyHashes, blocked => Assert.Matches("^[0-9a-f]{64}$", blocked));
    }
}
