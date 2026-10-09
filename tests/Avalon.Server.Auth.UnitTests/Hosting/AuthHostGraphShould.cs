using System.Reflection;
using Avalon.Hosting;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avalon.Server.Auth.UnitTests.Hosting;

/// <summary>
/// The auth host composed exactly as its entry point composes it, built under the validation
/// every Avalon host now enables. Its packet manager, a factory registration that building the host
/// does not run, is resolved too: it builds every handler's factory at startup (#866).
/// </summary>
public class AuthHostGraphShould
{
    [Fact]
    public async Task Build_with_no_captured_scoped_services()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.Auth);
            builder.Services.AddHostedService<AuthServer>();
            builder.Services.AddAuthServices();

            using IHost host = builder.Build();

            // Every auth packet but CMSG_REGISTER (no handler, kept as is) gets a factory, and the
            // factory builds its handler from a dispatch scope, as ServerBase.CallListener does.
            IPacketManager packets = host.Services.GetRequiredService<IPacketManager>();
            NetworkPacketType[] authPackets = typeof(Packet).Assembly.GetExportedTypes()
                .Select(type => type.GetCustomAttribute<PacketAttribute>())
                .Where(packet => packet?.HandleOn == ComponentType.Auth)
                .Select(packet => packet!.Type)
                .ToArray();
            Assert.NotEmpty(authPackets);

            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            var unserved = new List<NetworkPacketType>();
            foreach (NetworkPacketType type in authPackets)
            {
                if (!packets.TryGetPacketInfo(type, out PacketInfo info) || info.HandlerFactory is null)
                {
                    unserved.Add(type);
                    continue;
                }

                Assert.IsAssignableFrom<IPacketHandlerNew>(info.HandlerFactory(scope.ServiceProvider, null));
            }

            Assert.Equal([NetworkPacketType.CMSG_REGISTER], unserved);
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
