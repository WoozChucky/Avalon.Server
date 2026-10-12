namespace Avalon.Server.World.UnitTests.Telemetry;

// EventSource listeners enable the producer process-wide; keep unrelated allocation tests outside that window.
[CollectionDefinition("World tick trace", DisableParallelization = true)]
public class WorldTickTraceCollection;
