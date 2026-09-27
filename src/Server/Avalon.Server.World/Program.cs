using Avalon.Hosting;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.Presence;
using Avalon.World;
using Avalon.World.Configuration;
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
            // IWorld.InstanceRegistry does not exist until World.LoadAsync runs inside
            // WorldServer.ExecuteAsync, which happens after every IHostedService below has
            // already been constructed. The accessor defers that lookup to each capture tick
            // instead of resolving it once, eagerly, to null. See PresenceSnapshotService's
            // second constructor for the full explanation.
            .AddHostedService(provider => new PresenceSnapshotService(
                () => provider.GetRequiredService<IWorld>().InstanceRegistry,
                provider.GetRequiredService<IReplicatedCache>(),
                provider.GetRequiredService<IOptions<GameConfiguration>>(),
                provider.GetRequiredService<ILogger<PresenceSnapshotService>>()));

        IHost host = hostBuilder.Build();

        await WorldStartup.PrepareAsync(host);

        await AvalonHostBuilder.RunAsync<Program>(host, CancellationToken.None);
    }
}
