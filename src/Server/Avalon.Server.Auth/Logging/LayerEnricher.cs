using Serilog.Core;
using Serilog.Events;

namespace Avalon.Server.Auth.Logging;

public class LayerEnricher : ILogEventEnricher
{
    private const string LayerPropertyName = "Layer";
    private const string SourceContextPropertyName = "SourceContext";
    
    private readonly ScalarValue _serverLayer = new("SERVER");
    private readonly ScalarValue _networkLayer = new("NET");
    private readonly ScalarValue _cacheLayer = new("CACHE");
    
    private readonly List<string> _cacheNamespaces =
    [
        typeof(Avalon.Infrastructure.IReplicatedCache).FullName!,
        typeof(Avalon.Infrastructure.ReplicatedCache).FullName!
    ];
    
    private readonly List<string> _networkNamespaces =
    [
        typeof(Avalon.Infrastructure.Auth.AvalonAuthNetworkDaemon).FullName!,
        typeof(Avalon.Network.Tcp.AvalonSslTcpServer).FullName!,
        typeof(Avalon.Auth.AuthSessionManager).FullName!,
        typeof(Avalon.Infrastructure.AvalonSession).FullName!,
        typeof(Avalon.Network.TcpClient).FullName!
    ];

    private readonly List<string> _authNamespaces =
    [
        typeof(Avalon.Auth.AvalonAuthSession).FullName!,
        typeof(Avalon.Auth.AvalonAuth).FullName!
    ];

    private readonly List<string> _serverNamespaces =
    [
        typeof(Program).FullName!,
        typeof(Avalon.Infrastructure.Auth.AvalonAuthInfrastructure).FullName!
    ];
    
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!logEvent.Properties.TryGetValue(SourceContextPropertyName, out var sourceObject))
        {
            return;
        }
        
        var fullNamespace = sourceObject.ToString().Trim('"');

        if (_networkNamespaces.Contains(fullNamespace))
        {
            var layerProperty = new LogEventProperty(LayerPropertyName, _networkLayer);
            logEvent.AddPropertyIfAbsent(layerProperty);
            return;
        }
        
        if (_authNamespaces.Contains(fullNamespace))
        {
            var layerProperty = new LogEventProperty(LayerPropertyName, new ScalarValue($"AUTH/{fullNamespace}"));
            logEvent.AddPropertyIfAbsent(layerProperty);
            return;
        }
        
        if (_serverNamespaces.Contains(fullNamespace))
        {
            var layerProperty = new LogEventProperty(LayerPropertyName, _serverLayer);
            logEvent.AddPropertyIfAbsent(layerProperty);
            return;
        }
        
        if (_cacheNamespaces.Contains(fullNamespace))
        {
            var layerProperty = new LogEventProperty(LayerPropertyName, _cacheLayer);
            logEvent.AddPropertyIfAbsent(layerProperty);
            return;
        }
        
        var defaultLayerProperty = new LogEventProperty(LayerPropertyName, new ScalarValue($"{fullNamespace} (Not Filtered)"));
        logEvent.AddPropertyIfAbsent(defaultLayerProperty);
    }
}
