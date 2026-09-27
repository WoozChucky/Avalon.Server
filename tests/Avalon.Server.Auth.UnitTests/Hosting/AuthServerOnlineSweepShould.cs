using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Hosting;

/// <summary>
/// #555: the liveness sweep is wired into the running auth server: it starts after the start-up
/// reset, on the configured interval, and ends when the server stops.
/// </summary>
public sealed class AuthServerOnlineSweepShould : IDisposable
{
    private const string CertificatePassword = "test";
    private readonly string _certificatePath = Path.Combine(Path.GetTempPath(), $"avalon-auth-{Guid.NewGuid():N}.pfx");

    public AuthServerOnlineSweepShould()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=avalon-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate =
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        File.WriteAllBytes(_certificatePath, certificate.Export(X509ContentType.Pkcs12, CertificatePassword));
    }

    public void Dispose() => File.Delete(_certificatePath);

    [Fact]
    public async Task Sweep_after_the_start_up_reset_and_stop_with_the_server()
    {
        var accounts = Substitute.For<IAccountRepository>();
        accounts.ListOnlineSessionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var server = new AuthServer(Substitute.For<IServiceProvider>(), Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance, accounts, Substitute.For<IReplicatedCache>(),
            Options.Create(new HostingConfiguration { Port = 0, Host = "127.0.0.1" }),
            Options.Create(new HostingSecurity
            {
                CertificatePath = _certificatePath,
                CertificatePassword = CertificatePassword,
            }),
            Options.Create(new AuthConfiguration { OnlineSweepIntervalSeconds = 1 }));

        await server.StartAsync(CancellationToken.None);
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (accounts.ReceivedCalls().All(c => c.GetMethodInfo().Name != nameof(IAccountRepository.ListOnlineSessionsAsync))
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await server.StopAsync(stop.Token);

        Received.InOrder(() =>
        {
            accounts.MarkAllOfflineAsync(Arg.Any<CancellationToken>());
            accounts.ListOnlineSessionsAsync(Arg.Any<CancellationToken>());
        });
        Assert.NotNull(server.ExecuteTask);
        Assert.True(server.ExecuteTask!.IsCompletedSuccessfully);
    }
}
