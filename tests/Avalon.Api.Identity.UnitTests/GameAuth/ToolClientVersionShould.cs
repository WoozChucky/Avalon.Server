using System.Text.Json;
using Avalon.Configuration;
using Avalon.Infrastructure.GameAuth;
using Avalon.Tools;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// #949: the tools that sign in as a game client (tools/Shared/ToolClientVersion.cs, linked into this assembly) state a
/// version the API's default floor accepts, so raising Application:GameWorkloads:MinClientVersion in appsettings.json
/// without them fails the build rather than the next local run or load test.
/// </summary>
public sealed class ToolClientVersionShould
{
    [Fact]
    public void Meet_the_default_minimum_client_version_of_the_api()
    {
        string path = Path.Combine(FindRepositoryRoot(), "src", "Server", "Avalon.Api", "appsettings.json");
        using var settings = JsonDocument.Parse(File.ReadAllText(path));
        string floor = settings.RootElement.GetProperty("Application").GetProperty("GameWorkloads")
            .GetProperty("MinClientVersion").GetString()!;

        Assert.True(ClientVersionFloor.IsValid(floor), $"appsettings.json's MinClientVersion \"{floor}\" does not parse.");
        Assert.True(new GameWorkloadConfiguration { MinClientVersion = floor }.Admits(ToolClientVersion.Current),
            $"tools/Shared/ToolClientVersion.cs states {ToolClientVersion.Current}, below the API's default floor {floor}.");
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
