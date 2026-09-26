using Avalon.Database.Auth;
using Avalon.Hosting;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
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
            await using AuthDbContext db = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContextAsync(CancellationToken.None);
            ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
            // For comparison with the API's line: both count on the same Redis budgets (#478 review).
            LoginLimitsValidation.LogAtStartup(logger, host.Services.GetRequiredService<ILoginLimits>(), "Application");
            logger.LogInformation("Migrating database if necessary...");
            // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
            await db.Database.MigrateAsync(CancellationToken.None);

            IReplicatedCache cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
            await cache.ConnectAsync();
        }


        await AvalonHostBuilder.RunAsync<Program>(host, CancellationToken.None);
    }
}
