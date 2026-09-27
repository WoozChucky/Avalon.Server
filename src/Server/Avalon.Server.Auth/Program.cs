using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.Auth.Extensions;

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

        await AuthStartup.PrepareAsync(host);

        await AvalonHostBuilder.RunAsync<Program>(host, CancellationToken.None);
    }
}
