using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Authentication.Jwt;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Identity.Config;
using Avalon.Api.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Authentication;

/// <summary>
/// The keys that sign and validate the api's access tokens come from configuration that is not committed (#482), and
/// only identity holds the private one (#801). Startup refuses a key it cannot use, a private key in a process that does
/// not sign, and a process with no key to validate with, naming the setting to set; the HS256 key of before #801 keeps
/// its rules while it is set. Every key here is made in code; nothing depends on a machine secret.
/// </summary>
public class JwtSigningKeyShould
{
    private const string SettingName = "Application:Authentication:IssuerSigningKey";
    private const string EnvironmentVariableName = "Application__Authentication__IssuerSigningKey";

    private static IServiceCollection StartWith(string? issuerSigningKey)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Application:Authentication:SigningKey"] = ApiTestHost.SigningKey,
            ["Application:Authentication:SigningKeyId"] = ApiTestHost.SigningKeyId,
            ["Application:Authentication:Issuer"] = "Avalon Authentication System",
            ["Application:Authentication:Audience"] = "https://api.avalon.monster",
            ["Application:Authentication:ValidateIssuer"] = "true",
            ["Application:Authentication:ValidateAudience"] = "true",
        };
        if (issuerSigningKey is not null) settings[SettingName] = issuerSigningKey;

        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var applicationConfig = ApplicationConfig.Bind(configuration);

        var services = new ServiceCollection();
        services.AddAuth(applicationConfig);
        return services;
    }

    private static void AssertNamesTheSetting(Exception ex)
    {
        Assert.Contains(SettingName, ex.Message, StringComparison.Ordinal);
        Assert.Contains(EnvironmentVariableName, ex.Message, StringComparison.Ordinal);
    }

    private static string Sha256Hex(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>
    /// Each refusal of the token keys, in a process running <paramref name="service"/> alone with
    /// <c>Application:Authentication:</c><paramref name="setting"/> set to the value <paramref name="value"/> describes
    /// (or removed, for null): identity without a usable private key or key id, or listing another key under its own key
    /// id; a service that does not sign holding the private key, listing no public key, or listing one that does not parse
    /// or is private. Each names the setting, and none repeats a key.
    /// </summary>
    [Theory]
    [InlineData("identity", "SigningKey", null, "Application:Authentication:SigningKey")]
    [InlineData("identity", "SigningKey", "not a key", "Application:Authentication:SigningKey")]
    [InlineData("identity", "SigningKey", "its public key", "Application:Authentication:SigningKey")]
    [InlineData("identity", "SigningKey", "a P-384 private key", "Application:Authentication:SigningKey")]
    [InlineData("identity", "SigningKeyId", null, "Application:Authentication:SigningKeyId")]
    [InlineData("identity", "SigningKeyId", "not a name", "Application:Authentication:SigningKeyId")]
    [InlineData("identity", "ValidationKeys:test", "another public key", "Application:Authentication:ValidationKeys:test")]
    [InlineData("worlds", "SigningKey", "identity's private key", "Application:Authentication:SigningKey")]
    [InlineData("commerce", "ValidationKeys:test", null, "Application:Authentication:ValidationKeys")]
    [InlineData("distribution", "ValidationKeys:test", "not a key", "Application:Authentication:ValidationKeys:test")]
    [InlineData("worlds", "ValidationKeys:test", "identity's private key", "Application:Authentication:ValidationKeys:test")]
    public void Refuse_to_start_without_usable_token_keys_naming_the_setting(string service, string setting, string? value,
        string named)
    {
        string? key = value switch
        {
            null => null,
            "its public key" => PemEncoding.WriteString("PUBLIC KEY", Convert.FromBase64String(ApiTestHost.PublicKey)),
            "a P-384 private key" => ECDsa.Create(ECCurve.NamedCurves.nistP384).ExportPkcs8PrivateKeyPem(),
            "another public key" => Convert.ToBase64String(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo()),
            "identity's private key" => ApiTestHost.SigningKey,
            _ => value,
        };

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            AvalonApiHost.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production }, ApiServices.All, b =>
            {
                b.WebHost.UseTestServer();
                b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(ApiTestHost.SettingsFor(ApiServices.All,
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [ApiServiceSelection.Setting + ":0"] = service,
                        ["Application:Authentication:" + setting] = key,
                    }));
            }));

        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiTestHost.PublicKey, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_to_start_when_the_key_is_under_32_bytes()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => StartWith(new string('k', 31)));

        AssertNamesTheSetting(ex);
        Assert.Contains("31 bytes", ex.Message, StringComparison.Ordinal);
        Assert.Contains("at least 32", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "\n")]
    [InlineData("", "\r\n")]
    [InlineData("", " ")]
    [InlineData(" ", "")]
    public void Refuse_to_start_when_the_key_has_leading_or_trailing_whitespace(string leading, string trailing)
    {
        // A key read from a file often ends in a newline; it would sign with bytes nobody meant.
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            StartWith(leading + new string('k', 64) + trailing));

        AssertNamesTheSetting(ex);
        Assert.Contains("whitespace", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_bytes_not_characters()
    {
        // 16 characters, 32 bytes in UTF-8: long enough.
        StartWith(new string('é', 16));

        // 31 characters, one of them two bytes: 32 bytes, long enough.
        StartWith(new string('k', 30) + "é");
    }

    [Fact]
    public void Refuse_a_key_whose_hash_is_blocked()
    {
        const string MadeUpPublicKey = "made-up-public-key-made-up-public-key-0123456789";
        var config = new AuthenticationConfig { IssuerSigningKey = MadeUpPublicKey };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            JwtSigningKey.Create(config, [Sha256Hex(MadeUpPublicKey)]));

        AssertNamesTheSetting(ex);
        Assert.Contains("public", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_a_key_whose_hash_is_not_blocked()
    {
        var config = new AuthenticationConfig { IssuerSigningKey = new string('k', 64) };

        SymmetricSecurityKey? key = JwtSigningKey.Create(config, [Sha256Hex(new string('j', 64))]);

        Assert.Equal(Encoding.UTF8.GetBytes(new string('k', 64)), key?.Key);
    }

    [Fact]
    public void Block_the_formerly_committed_key_in_production()
    {
        // The value itself is not in the tree; its hash is. An empty list would block nothing.
        Assert.NotEmpty(JwtSigningKey.BlockedKeyHashes);
        Assert.All(JwtSigningKey.BlockedKeyHashes, hash => Assert.Matches("^[0-9a-f]{64}$", hash));
    }

    [Fact]
    public void Always_validate_the_issuer_signing_key()
    {
        using ServiceProvider provider = StartWith(new string('k', 64)).AddLogging().BuildServiceProvider();

        TokenValidationParameters validation = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        Assert.True(validation.ValidateIssuerSigningKey);
    }

    [Fact]
    public void Not_be_committed_in_any_api_appsettings_file()
    {
        string apiDir = Path.Combine(FindRepositoryRoot(), "src", "Server", "Avalon.Api");
        string[] files = Directory.GetFiles(apiDir, "appsettings*.json", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(files);

        foreach (string path in files)
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(path));

            // Absent, not empty placeholders, so nobody is invited to fill them in.
            foreach (string[] secret in new[]
                     {
                         new[] { "Authentication", nameof(TokenValidationConfig.SigningKey) },
                         new[] { "Authentication", nameof(TokenValidationConfig.IssuerSigningKey) },
                         new[] { "GameAuth", nameof(GameAuthConfig.HostKey) },
                     })
            {
                Assert.False(Has(settings.RootElement, ["Application", .. secret]),
                    $"{Path.GetFileName(path)} must not carry Application:{string.Join(':', secret)}; set it through user-secrets or the environment.");
            }
        }
    }

    private static bool Has(JsonElement element, string[] path) =>
        path.Length == 0 || (element.TryGetProperty(path[0], out JsonElement child) && Has(child, path[1..]));

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Avalon.sln"))) return dir.FullName;
        }

        throw new InvalidOperationException("Avalon.sln not found above " + AppContext.BaseDirectory);
    }
}
