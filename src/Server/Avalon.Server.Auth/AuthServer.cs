using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Avalon.Configuration;
using Avalon.Hosting;
using Avalon.Hosting.Networking;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth;

public class AuthServer : ServerBase<AuthConnection>
{
    public X509Certificate2 Certificate { get; private set; }

    private readonly HostingSecurity _securityOptions;
    private readonly ConcurrentDictionary<Type, (PropertyInfo packetProperty, PropertyInfo connectionProperty)> _propertyCache = new();

    public AuthServer(IPacketManager packetManager, ILoggerFactory loggerFactory, PluginExecutor pluginExecutor,
        IServiceProvider serviceProvider,
        IOptions<HostingConfiguration> hostingOptions, IOptions<HostingSecurity> securityOptions)
        : base(packetManager, loggerFactory.CreateLogger<AuthServer>(), pluginExecutor, serviceProvider, hostingOptions)
    {
        _securityOptions = securityOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverCertBytes = await File.ReadAllBytesAsync(_securityOptions.CertificatePath, stoppingToken);
        
        Certificate = new X509Certificate2(serverCertBytes, _securityOptions.CertificatePassword);
        
        RegisterNewConnectionListener(NewConnection);
    }
    
    private bool NewConnection(IConnection connection)
    {
        return true;
    }

    protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
    {
        // Check if the cache contains the property accessors for the given packet type
        if (!_propertyCache.TryGetValue(packetType, out var cachedProperties))
        {
            // Cache miss: Reflect the properties
            var contextPacketProperty = typeof(AuthPacketContext<>).MakeGenericType(packetType)
                .GetProperty(nameof(AuthPacketContext<object>.Packet))!;
            var contextConnectionProperty = typeof(AuthPacketContext<>).MakeGenericType(packetType)
                .GetProperty(nameof(AuthPacketContext<object>.Connection))!;

            // Cache the reflected properties
            cachedProperties = (contextPacketProperty, contextConnectionProperty);
            _propertyCache[packetType] = cachedProperties;
        }

        // Create a new context instance
        var context = Activator.CreateInstance(typeof(AuthPacketContext<>).MakeGenericType(packetType))!;
    
        // Set the packet and connection properties
        cachedProperties.packetProperty.SetValue(context, packet);
        cachedProperties.connectionProperty.SetValue(context, connection);
    
        return context;
    }
}
