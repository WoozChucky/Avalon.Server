using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Avalon.Server;

public class AllocationsListener : EventListener
{
    private readonly ILogger<AllocationsListener> _logger;

    // from https://docs.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events
    private const int GC_KEYWORD =                 0x0000001;
    private const int TYPE_KEYWORD =               0x0080000;
    private const int GCHEAPANDTYPENAMES_KEYWORD = 0x1000000;

    public AllocationsListener(ILogger<AllocationsListener> logger)
    {
        _logger = logger;
    }
    
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // look for .NET Garbage Collection events
        if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime"))
        {
            _logger?.LogTrace("{EventSourceGuid} | {EventSourceName}", eventSource.Guid, eventSource.Name);
            
            EnableEvents(
                eventSource, 
                EventLevel.Verbose, 
                (EventKeywords) (GC_KEYWORD | GCHEAPANDTYPENAMES_KEYWORD | TYPE_KEYWORD)
            );
        }
    }
            
    // Called whenever an event is written.
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // Write the contents of the event to the logger.
        _logger?.LogTrace("ThreadID = {EventDataOsThreadId} ID = {EventDataEventId} Name = {EventDataEventName}", eventData.OSThreadId, eventData.EventId, eventData.EventName);
        for (var i = 0; i < eventData.Payload!.Count; i++)
        {
            var payloadString = eventData.Payload[i] != null ? eventData.Payload[i]!.ToString() : string.Empty;
            _logger?.LogTrace("    Name = \"{EventDataPayloadName}\" Value = \"{EventDataPayloadValue}\"", eventData.PayloadNames[i], payloadString);
        }
    }
}
