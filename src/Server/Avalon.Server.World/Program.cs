using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Hosting;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            .AddHostedService(provider => provider.GetRequiredService<WorldServer>());

        IHost host = hostBuilder.Build();

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            CharacterDbContext characterDb = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
            WorldDbContext worldDb = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            host.Services.GetRequiredService<ILogger<Program>>().LogInformation("Migrating database if necessary...");
            // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
            await characterDb.Database.MigrateAsync(CancellationToken.None);
            await worldDb.Database.MigrateAsync(CancellationToken.None);

            IReplicatedCache cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
            await cache.ConnectAsync();
        }

        await AvalonHostBuilder.RunAsync<Program>(host, CancellationToken.None);
    }
}
