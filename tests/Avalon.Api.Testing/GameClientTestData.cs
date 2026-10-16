using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Testing;

/// <summary>The client version game sign-in requires in the tests, and the client that meets it (#949).</summary>
public static class GameClientTestData
{
    /// <summary><c>Application:GameWorkloads:MinClientVersion</c>, as the API's appsettings.json has it.</summary>
    public const string MinClientVersion = "0.4.0";

    /// <summary>The version a test's client states: the floor itself.</summary>
    public const string ClientVersion = MinClientVersion;

    /// <summary>The game workload bindings <paramref name="servers"/>, with <see cref="MinClientVersion"/>.</summary>
    public static IOptions<GameWorkloadConfiguration> Workloads(params GameServerDefinition[] servers) =>
        Options.Create(new GameWorkloadConfiguration { MinClientVersion = MinClientVersion, Servers = [.. servers] });
}
