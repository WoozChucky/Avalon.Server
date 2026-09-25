using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// The key that signs and validates the api's JWTs comes from configuration that is not committed
/// (#482). Startup refuses a missing, too short, padded or known-public key and names the setting
/// to set. Every key here is made up in code; nothing depends on a machine secret.
/// </summary>
public class JwtSigningKeyShould
{
    private const string SettingName = "Application:Authentication:IssuerSigningKey";
    private const string EnvironmentVariableName = "Application__Authentication__IssuerSigningKey";

    private static IServiceCollection StartWith(string? signingKey)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Application:Authentication:Issuer"] = "Avalon Authentication System",
            ["Application:Authentication:Audience"] = "https://api.avalon.monster",
            ["Application:Authentication:ValidateIssuer"] = "true",
            ["Application:Authentication:ValidateAudience"] = "true",
        };
        if (signingKey is not null) settings[SettingName] = signingKey;

        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ApplicationConfig applicationConfig = ApiConfiguration.Bind(configuration);

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

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData(" ")]
    public void Refuse_to_start_when_the_key_has_trailing_whitespace(string trailing)
    {
        // A key read from a file often ends in a newline; it would sign with bytes nobody meant.
        var ex = Assert.Throws<InvalidOperationException>(() => StartWith(new string('k', 64) + trailing));

        AssertNamesTheSetting(ex);
        Assert.Contains("whitespace", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_to_start_when_the_key_has_a_leading_space()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StartWith(" " + new string('k', 64)));

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
    public void Start_with_a_key_of_exactly_32_bytes() => StartWith(new string('k', 32));

    [Fact]
    public void Refuse_a_key_whose_hash_is_blocked()
    {
        const string madeUpPublicKey = "made-up-public-key-made-up-public-key-0123456789";
        var config = new AuthenticationConfig { IssuerSigningKey = madeUpPublicKey };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtSigningKey.Create(config, [Sha256Hex(madeUpPublicKey)]));

        AssertNamesTheSetting(ex);
        Assert.Contains("public", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_a_key_whose_hash_is_not_blocked()
    {
        var config = new AuthenticationConfig { IssuerSigningKey = new string('k', 64) };

        SymmetricSecurityKey key = JwtSigningKey.Create(config, [Sha256Hex(new string('j', 64))]);

        Assert.Equal(Encoding.UTF8.GetBytes(new string('k', 64)), key.Key);
    }

    [Fact]
    public void Block_the_formerly_committed_key_in_production()
    {
        // The value itself is not in the tree; its hash is. An empty list would block nothing.
        Assert.NotEmpty(JwtSigningKey.BlockedKeyHashes);
        Assert.All(JwtSigningKey.BlockedKeyHashes, hash => Assert.Matches("^[0-9a-f]{64}$", hash));
    }

    [Fact]
    public void Share_one_key_between_signing_and_validation()
    {
        IServiceCollection services = StartWith(new string('k', 64));
        services.AddLogging();
        services.AddSingleton(new AuthenticationConfig { IssuerSigningKey = new string('k', 64) });
        services.AddScoped<IJwtUtils, JwtUtils>();
        using ServiceProvider provider = services.BuildServiceProvider();

        var registered = provider.GetRequiredService<SymmetricSecurityKey>();
        TokenValidationParameters validation = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        Assert.Same(registered, validation.IssuerSigningKey);
        Assert.Same(registered, provider.GetRequiredService<SymmetricSecurityKey>());
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
        var otherSigner = new JwtUtils(otherKey, JwtSigningKey.Create(otherKey));

        using HttpResponseMessage response = await host.GetAsync("/player", otherSigner.GenerateJwtToken(account));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Not_be_committed_in_any_api_appsettings_file()
    {
        string apiDir = Path.Combine(FindRepositoryRoot(), "src", "Server", "Avalon.Api");
        string[] files = Directory.GetFiles(apiDir, "appsettings*.json", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(files);

        foreach (string path in files)
        {
            using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(path));

            // Absent, not an empty placeholder, so nobody is invited to fill it in.
            Assert.False(HasSigningKey(settings.RootElement),
                $"{Path.GetFileName(path)} must not carry IssuerSigningKey; set it through user-secrets or the environment.");
        }
    }

    private static bool HasSigningKey(JsonElement root) =>
        root.TryGetProperty("Application", out JsonElement application)
        && application.TryGetProperty("Authentication", out JsonElement authentication)
        && authentication.TryGetProperty("IssuerSigningKey", out _);

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Avalon.sln"))) return dir.FullName;
        }

        throw new InvalidOperationException("Avalon.sln not found above " + AppContext.BaseDirectory);
    }
}
