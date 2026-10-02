using Avalon.Hosting;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.Presence;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Presence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Thread.CurrentThread.Name ??= "main";

        HostApplicationBuilder hostBuilder = await AvalonHostBuilder.CreateHostAsync(args, ComponentType.World);
        hostBuilder.ConfigureOpenTelemetry();
        hostBuilder.Services
            .AddWorldServices()
            .AddSingleton<WorldServer>()
            .AddSingleton<IWorldServer>(provider => provider.GetRequiredService<WorldServer>())
            .AddHostedService(provider => provider.GetRequiredService<WorldServer>())
            .AddHostedService<WorldMaintenanceReconciler>()
            .AddHostedService(provider =>
            {
                WorldServer server = provider.GetRequiredService<WorldServer>();
                return new WorldReadyHeartbeatService(
                    provider.GetRequiredService<IOptions<GameConfiguration>>().Value.WorldId.Value,
                    () => server.IsListening,
                    () => server.CompletedTicks,
                    provider.GetRequiredService<IReplicatedCache>(),
                    provider.GetRequiredService<ILogger<WorldReadyHeartbeatService>>());
            })
            // Writes to Redis what WorldServer captures on the tick (#639).
            .AddHostedService(provider => new PresenceSnapshotService(
                provider.GetRequiredService<PresenceCapture>(),
                provider.GetRequiredService<IReplicatedCache>(),
                provider.GetRequiredService<ILogger<PresenceSnapshotService>>()));

        IHost host = hostBuilder.Build();

        await WorldStartup.PrepareAsync(host);

        await AvalonHostBuilder.RunAsync<Program>(host, CancellationToken.None);
    }
}
