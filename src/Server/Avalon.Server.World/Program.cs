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
            .AddHostedService<WorldServer>()
            .AddSingleton<IWorldServer>(provider =>
                provider.GetServices<IHostedService>().OfType<WorldServer>().Single());

        IHost host = hostBuilder.Build();

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            CharacterDbContext characterDb = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
            WorldDbContext worldDb = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            host.Services.GetRequiredService<ILogger<Program>>().LogInformation("Migrating database if necessary...");
            await characterDb.Database.MigrateAsync();
            await worldDb.Database.MigrateAsync();

            IReplicatedCache cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
            await cache.ConnectAsync();
        }

        await AvalonHostBuilder.RunAsync<Program>(host);
    }
}
