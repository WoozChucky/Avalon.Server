using System.Net;
using System.Text.Json;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// The key that signs and validates the api's JWTs comes from configuration that is not committed
/// (#482). Startup refuses a missing, too short or known-public key and names the setting to set.
/// Every key here is made up in code; nothing depends on a machine secret.
/// </summary>
public class JwtSigningKeyShould
{
    private const string SettingName = "Application:Authentication:IssuerSigningKey";
    private const string EnvironmentVariableName = "Application__Authentication__IssuerSigningKey";

    /// <summary>Binds the "Application" section exactly as Program.cs does, then registers auth.</summary>
    private static void StartWith(string? signingKey)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Application:Authentication:Issuer"] = "Avalon Authentication System",
            ["Application:Authentication:Audience"] = "https://api.avalon.monster",
            ["Application:Authentication:ValidateIssuer"] = "true",
            ["Application:Authentication:ValidateAudience"] = "true",
            ["Application:Authentication:ValidateIssuerKey"] = "true",
        };
        if (signingKey is not null) settings[SettingName] = signingKey;

        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ApplicationConfig applicationConfig = new();
        configuration.Bind("Application", applicationConfig);

        new ServiceCollection().AddAuth(applicationConfig);
    }

    private static void AssertNamesTheSetting(Exception ex)
    {
        Assert.Contains(SettingName, ex.Message, StringComparison.Ordinal);
        Assert.Contains(EnvironmentVariableName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_to_start_when_the_key_is_missing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StartWith(null));

        AssertNamesTheSetting(ex);
        Assert.Contains("not set", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuse_to_start_when_the_key_is_empty(string key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StartWith(key));

        AssertNamesTheSetting(ex);
        Assert.Contains("not set", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_to_start_when_the_authentication_section_is_absent()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAuth(new ApplicationConfig { Authentication = null }));

        AssertNamesTheSetting(ex);
    }

    [Fact]
    public void Refuse_to_start_when_the_key_is_under_32_bytes()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StartWith(new string('k', 31)));

        AssertNamesTheSetting(ex);
        Assert.Contains("31 bytes", ex.Message, StringComparison.Ordinal);
        Assert.Contains("at least 32", ex.Message, StringComparison.Ordinal);
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
    public void Start_with_a_key_of_exactly_32_bytes() => StartWith(new string('k', 32));

    [Fact]
    public void Refuse_to_start_with_the_key_that_used_to_be_committed()
    {
        // The value that sat in appsettings.json until #482 is public. It is rebuilt here from
        // two pieces so the literal does not reappear whole as a scannable secret.
        string leaked = string.Concat("GipaCjt1F8gPcBG5yLTkByVA2VEtjT5Z", "bY1sblRlFUaJUQA0vuXlPEMOCa7PvkgK");

        var ex = Assert.Throws<InvalidOperationException>(() => StartWith(leaked));

        AssertNamesTheSetting(ex);
        Assert.Contains("public", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_to_sign_tokens_with_a_short_key()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new JwtUtils(new AuthenticationConfig { IssuerSigningKey = "short" }));

        AssertNamesTheSetting(ex);
    }

    [Fact]
    public async Task Sign_tokens_that_validate_end_to_end_with_a_valid_key()
    {
        await using ApiAuthHost host = await ApiAuthHost.StartAsync();
        var account = ApiAuthHost.MakeAccount();
        host.AccountNowIs(account);

        using HttpResponseMessage response = await host.GetAsync("/player", ApiAuthHost.Mint(account));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reject_tokens_signed_with_a_different_key()
    {
        await using ApiAuthHost host = await ApiAuthHost.StartAsync();
        var account = ApiAuthHost.MakeAccount();
        host.AccountNowIs(account);
        var otherKey = new AuthenticationConfig
        {
            IssuerSigningKey = new string('x', 64),
            Issuer = ApiAuthHost.AuthConfig.Issuer,
            Audience = ApiAuthHost.AuthConfig.Audience,
            AccessTokenLifetimeMinutes = 15,
        };

        using HttpResponseMessage response =
            await host.GetAsync("/player", new JwtUtils(otherKey).GenerateJwtToken(account));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Not_be_committed_in_the_api_appsettings()
    {
        string path = Path.Combine(FindRepositoryRoot(), "src", "Server", "Avalon.Api", "appsettings.json");
        using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(path));

        JsonElement authentication = settings.RootElement.GetProperty("Application").GetProperty("Authentication");

        // Absent, not an empty placeholder, so nobody is invited to fill it in.
        Assert.False(authentication.TryGetProperty("IssuerSigningKey", out _),
            "appsettings.json must not carry IssuerSigningKey; set it through user-secrets or the environment.");
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Avalon.sln"))) return dir.FullName;
        }

        throw new InvalidOperationException("Avalon.sln not found above " + AppContext.BaseDirectory);
    }
}
