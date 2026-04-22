using Avalon.Database.Auth;
using Avalon.Hosting;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.Auth.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.Auth;

public class Program
{
    private static async Task Main(string[] args)
    {
        HostApplicationBuilder hostBuilder = await AvalonHostBuilder.CreateHostAsync(args, ComponentType.Auth);
        hostBuilder.ConfigureOpenTelemetry();
        hostBuilder.Services.AddHostedService<AuthServer>();
        hostBuilder.Services.AddAuthServices();

        IHost host = hostBuilder.Build();

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            AuthDbContext db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Migrating database if necessary...");
            // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
            await db.Database.MigrateAsync(CancellationToken.None);

            IReplicatedCache cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
            await cache.ConnectAsync();
        }


        await AvalonHostBuilder.RunAsync<Program>(host, CancellationToken.None);
    }
}
