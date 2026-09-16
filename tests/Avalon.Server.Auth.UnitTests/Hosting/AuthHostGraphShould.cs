using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Hosting;

/// <summary>
/// The auth host composed exactly as its entry point composes it, built under the validation
/// every Avalon host now enables.
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

            Assert.NotNull(host);
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
