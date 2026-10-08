using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Hosting;
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
/// not sign, and a process with no key to validate with, naming the setting to set. Every key here is made in code;
/// nothing depends on a machine secret.
/// </summary>
public class JwtSigningKeyShould
{
    private static IServiceCollection StartWithTheKeys()
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

        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var applicationConfig = ApplicationConfig.Bind(configuration);

        var services = new ServiceCollection();
        services.AddAuth(applicationConfig);
        return services;
    }

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
    public void Always_validate_the_issuer_signing_key()
    {
        using ServiceProvider provider = StartWithTheKeys().AddLogging().BuildServiceProvider();

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
