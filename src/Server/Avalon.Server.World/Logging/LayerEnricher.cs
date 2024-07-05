using Serilog.Core;
using Serilog.Events;

namespace Avalon.Server.World.Logging;

public class LayerEnricher : ILogEventEnricher
{
    private const string LayerPropertyName = "Layer";
    private const string SourceContextPropertyName = "SourceContext";
    
    private readonly ScalarValue _serverLayer = new("SERVER");
    private readonly ScalarValue _networkLayer = new("NET");
    
    private readonly List<string> _networkNamespaces =
    [
        typeof(Avalon.Infrastructure.World.AvalonWorldNetworkDaemon).FullName!,
        typeof(Avalon.Network.Tcp.AvalonTcpServer).FullName!,
        typeof(Avalon.Game.AvalonSessionManager).FullName!,
        typeof(Avalon.Network.TcpClient).FullName!,
    ];

    private readonly List<string> _gameNamespaces =
    [
        typeof(Avalon.Game.AvalonGame).FullName!,
        typeof(Avalon.Game.AvalonWorldSession).FullName!,
    ];

    private readonly List<string> _serverNamespaces =
    [
        typeof(Program).FullName!,
        typeof(Avalon.Infrastructure.World.AvalonWorldInfrastructure).FullName!
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
        
        if (_gameNamespaces.Contains(fullNamespace))
        {
            var layerProperty = new LogEventProperty(LayerPropertyName, new ScalarValue($"GAME/{fullNamespace}"));
            logEvent.AddPropertyIfAbsent(layerProperty);
            return;
        }
        
        if (_serverNamespaces.Contains(fullNamespace))
        {
            var layerProperty = new LogEventProperty(LayerPropertyName, _serverLayer);
            logEvent.AddPropertyIfAbsent(layerProperty);
            return;
        }
        
        var defaultLayerProperty = new LogEventProperty(LayerPropertyName, new ScalarValue($"{fullNamespace} (Not Filtered)"));
        logEvent.AddPropertyIfAbsent(defaultLayerProperty);
    }
}
