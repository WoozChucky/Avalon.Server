using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Generic;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth;

public class AuthServer(
    IServiceProvider serviceProvider,
    IPacketManager packetManager,
    ILoggerFactory loggerFactory,
    IAccountRepository accountRepository,
    IOptions<HostingConfiguration> hostingOptions,
    IOptions<HostingSecurity> securityOptions)
    : ServerBase<AuthConnection>(packetManager, loggerFactory.CreateLogger<AuthServer>(),
        serviceProvider, hostingOptions)
{
    private static readonly MethodInfo s_buildContextMethod =
        typeof(AuthServer).GetMethod(nameof(BuildContextFactory), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"Could not reflect {nameof(AuthServer)}.{nameof(BuildContextFactory)}. " +
            "Ensure the method is non-public, static, and not overloaded.");

    private readonly ConcurrentDictionary<Type, Func<IConnection, Packet?, object>>
        _contextFactoryCache = new();

    private readonly HostingSecurity _securityOptions = securityOptions.Value;

    public new ImmutableArray<IAuthConnection> Connections =>
        TypedConnections.CastArray<IAuthConnection>();

    public X509Certificate2 Certificate { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        byte[] serverCertBytes = await File.ReadAllBytesAsync(_securityOptions.CertificatePath, stoppingToken);

        Certificate = X509CertificateLoader.LoadPkcs12(serverCertBytes, _securityOptions.CertificatePassword);

        // Reset account online status
        IList<Account> accounts = await accountRepository.FindAllAsync(cancellationToken: stoppingToken);
        foreach (Account account in accounts)
        {
            account.Online = false;
            await accountRepository.UpdateAsync(account, stoppingToken);
        }

        RegisterNewConnectionListener(NewConnection);
    }

    protected override Task OnStoppingAsync(CancellationToken stoppingToken)
    {
        foreach (IAuthConnection connection in Connections)
            GracefulShutdownHelper.NotifyAndClose(connection, "Server is shutting down", DisconnectReason.ServerShutdown);

        return Task.CompletedTask;
    }

    private bool NewConnection(IConnection connection) => true;

    protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
    {
        var factory = _contextFactoryCache.GetOrAdd(packetType, static t =>
            (Func<IConnection, Packet?, object>)s_buildContextMethod.MakeGenericMethod(t).Invoke(null, null)!);
        return factory(connection, packet as Packet);
    }

    private static Func<IConnection, Packet?, object> BuildContextFactory<TPacket>() where TPacket : Packet
        => static (conn, pkt) => new AuthPacketContext<TPacket>
            { Connection = (IAuthConnection)conn!, Packet = (TPacket)pkt! };
}
