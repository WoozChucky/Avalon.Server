using Avalon.Api.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Avalon.Api.UnitTests;

/// <summary>
/// Program.cs takes its configuration from <see cref="ApiConfiguration.Sources"/>. Every source the
/// host layers above appsettings.json (user-secrets, the environment, the command line) must still
/// win over it: the JWT signing key lives in user-secrets locally and in the environment elsewhere,
/// and re-adding appsettings.json on top once silently hid both (#482).
/// </summary>
public sealed class ApiConfigurationShould : IDisposable
{
    private const string Key = "Application:Authentication:IssuerSigningKey";
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "avalon-api-config-" + Guid.NewGuid().ToString("N"));

    public ApiConfigurationShould()
    {
        Directory.CreateDirectory(_contentRoot);
        File.WriteAllText(Path.Combine(_contentRoot, "appsettings.json"),
            """{ "Application": { "Authentication": { "IssuerSigningKey": "from-appsettings-json" } } }""");
    }

    public void Dispose() => Directory.Delete(_contentRoot, recursive: true);

    // "Testing" keeps the host from loading this machine's real user-secrets.
    private WebApplicationBuilder NewBuilder(params string[] args) => WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = _contentRoot,
        EnvironmentName = "Testing",
    });

    private static string? SigningKeyFrom(WebApplicationBuilder builder) =>
        ApiConfiguration.Bind(ApiConfiguration.Sources(builder)).Authentication?.IssuerSigningKey;

    [Fact]
    public void Read_appsettings_json_when_nothing_overrides_it() =>
        Assert.Equal("from-appsettings-json", SigningKeyFrom(NewBuilder()));

    [Fact]
    public void Let_a_source_added_after_appsettings_json_win()
    {
        // Stands in for user-secrets, which the host adds after appsettings.json in Development.
        WebApplicationBuilder builder = NewBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Key] = "from-user-secrets",
        });

        Assert.Equal("from-user-secrets", SigningKeyFrom(builder));
    }

    [Fact]
    public void Let_the_command_line_win() =>
        Assert.Equal("from-command-line", SigningKeyFrom(NewBuilder($"--{Key}=from-command-line")));
}
