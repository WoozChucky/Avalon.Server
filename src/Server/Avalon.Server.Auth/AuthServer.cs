using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
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
    private readonly ConcurrentDictionary<Type, (PropertyInfo packetProperty, PropertyInfo connectionProperty)>
        _propertyCache = new();

    private readonly HostingSecurity _securityOptions = securityOptions.Value;

    public new ImmutableArray<IAuthConnection> Connections =>
        [.. base.Connections.Values.Cast<AuthConnection>()];

    public X509Certificate2 Certificate { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        byte[] serverCertBytes = await File.ReadAllBytesAsync(_securityOptions.CertificatePath, stoppingToken);

        Certificate = X509CertificateLoader.LoadPkcs12(serverCertBytes, _securityOptions.CertificatePassword);

        // Reset account online status
        IList<Account> accounts = await accountRepository.FindAllAsync();
        foreach (Account account in accounts)
        {
            account.Online = false;
            await accountRepository.UpdateAsync(account);
        }

        RegisterNewConnectionListener(NewConnection);
    }

    protected override Task OnStoppingAsync(CancellationToken stoppingToken)
    {
        foreach (IAuthConnection connection in Connections)
        {
            connection.Send(SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown));
            connection.Close();
        }

        return Task.CompletedTask;
    }

    private bool NewConnection(IConnection connection) => true;

    protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
    {
        // Check if the cache contains the property accessors for the given packet type
        if (!_propertyCache.TryGetValue(packetType,
                out (PropertyInfo packetProperty, PropertyInfo connectionProperty) cachedProperties))
        {
            // Cache miss: Reflect the properties
            PropertyInfo contextPacketProperty = typeof(AuthPacketContext<>).MakeGenericType(packetType)
                .GetProperty(nameof(AuthPacketContext<object>.Packet))!;
            PropertyInfo contextConnectionProperty = typeof(AuthPacketContext<>).MakeGenericType(packetType)
                .GetProperty(nameof(AuthPacketContext<object>.Connection))!;

            // Cache the reflected properties
            cachedProperties = (contextPacketProperty, contextConnectionProperty);
            _propertyCache[packetType] = cachedProperties;
        }

        // Create a new context instance
        object context = Activator.CreateInstance(typeof(AuthPacketContext<>).MakeGenericType(packetType))!;

        // Set the packet and connection properties
        cachedProperties.packetProperty.SetValue(context, packet);
        cachedProperties.connectionProperty.SetValue(context, connection);

        return context;
    }
}
