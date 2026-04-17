using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Avalon.Server.Auth.Logging;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Logging;

public class LayerEnricherShould
{
    private readonly LayerEnricher _enricher = new();
    private readonly ILogEventPropertyFactory _factory = Substitute.For<ILogEventPropertyFactory>();
    private readonly MessageTemplateParser _parser = new();

    private LogEvent MakeLogEvent(string? sourceContext = null)
    {
        var mt = _parser.Parse("Test message");
        var properties = sourceContext is not null
            ? [new LogEventProperty("SourceContext", new ScalarValue(sourceContext))]
            : Enumerable.Empty<LogEventProperty>();
        return new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, mt, properties);
    }

    private static string? GetLayerValue(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue("Layer", out var value))
            return null;
        return (value as ScalarValue)?.Value as string;
    }

    [Fact]
    public void DoNotAddLayer_WhenNoSourceContextProperty()
    {
        var logEvent = MakeLogEvent(null);

        _enricher.Enrich(logEvent, _factory);

        Assert.False(logEvent.Properties.ContainsKey("Layer"));
    }

    [Fact]
    public void AddNotFilteredLayer_WhenSourceContextIsUnknownNamespace()
    {
        var logEvent = MakeLogEvent("Some.Completely.Unknown.Namespace");

        _enricher.Enrich(logEvent, _factory);

        Assert.Equal("Some.Completely.Unknown.Namespace (Not Filtered)", GetLayerValue(logEvent));
    }

    [Fact]
    public void AddNetLayer_WhenSourceIsAvalonTcpClient()
    {
        // Matches _networkNamespaces: Avalon.Network.TcpClient
        var logEvent = MakeLogEvent("Avalon.Network.TcpClient");

        _enricher.Enrich(logEvent, _factory);

        Assert.Equal("NET", GetLayerValue(logEvent));
    }

    [Fact]
    public void AddServerLayer_WhenSourceIsProgramClass()
    {
        // Matches _serverNamespaces: typeof(Program).FullName = "Avalon.Server.Auth.Program"
        var logEvent = MakeLogEvent("Avalon.Server.Auth.Program");

        _enricher.Enrich(logEvent, _factory);

        Assert.Equal("SERVER", GetLayerValue(logEvent));
    }

    [Fact]
    public void AddCacheLayer_WhenSourceIsReplicatedCacheInterface()
    {
        // Matches _cacheNamespaces: Avalon.Infrastructure.IReplicatedCache
        var logEvent = MakeLogEvent("Avalon.Infrastructure.IReplicatedCache");

        _enricher.Enrich(logEvent, _factory);

        Assert.Equal("CACHE", GetLayerValue(logEvent));
    }

    [Fact]
    public void AddCacheLayer_WhenSourceIsReplicatedCacheImpl()
    {
        // Matches _cacheNamespaces: Avalon.Infrastructure.ReplicatedCache
        var logEvent = MakeLogEvent("Avalon.Infrastructure.ReplicatedCache");

        _enricher.Enrich(logEvent, _factory);

        Assert.Equal("CACHE", GetLayerValue(logEvent));
    }

    [Fact]
    public void DoNotOverrideExistingLayer_WhenLayerAlreadyPresent()
    {
        var mt = _parser.Parse("Test");
        var properties = new[]
        {
            new LogEventProperty("SourceContext", new ScalarValue("Avalon.Network.Tcp.AvalonSslTcpServer")),
            new LogEventProperty("Layer", new ScalarValue("EXISTING"))
        };
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, mt, properties);

        _enricher.Enrich(logEvent, _factory);

        // AddPropertyIfAbsent should preserve the existing value
        Assert.Equal("EXISTING", GetLayerValue(logEvent));
    }
}
