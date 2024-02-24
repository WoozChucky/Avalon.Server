using Avalon.Common.Threading;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;

namespace Avalon.Infrastructure;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Handshake,
    Connected,
    TimedOut
}

public class AvalonSession : IDisposable
{
    public int AccountId { get; set; }
    public IRemoteSource Connection { get; }
    public int RoundTripTime { get; protected set; }
    public ConnectionStatus Status { get; set; }
    public DateTime LastUpdateAt { get; set; } = DateTime.UtcNow;
    
    private readonly RingBuffer<NetworkPacket> _packetQueue;
    private readonly ILogger<AvalonSession> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly SemaphoreSlim _sendSemaphore;
    
    public AvalonSession(ILoggerFactory loggerFactory, IRemoteSource connection)
    {
        _logger = loggerFactory.CreateLogger<AvalonSession>();
        _cts = new CancellationTokenSource();
        _packetQueue = new RingBuffer<NetworkPacket>("SEND", 1024);
        _sendSemaphore = new SemaphoreSlim(1, 1);
        Connection = connection;
        Task.Run(ProcessPacketsAsync);
    }
    
    public virtual Task SendAsync(NetworkPacket packet)
    {
        _packetQueue.Enqueue(packet);
        return Task.CompletedTask;
    }
    
    public virtual void Dispose()
    {
        Connection.Dispose();
    }
    
    protected virtual async Task ProcessPacketsAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var packet = await _packetQueue.DequeueAsync(_cts.Token);

                    if (packet is null)
                    {
                        _logger.LogWarning("Packet was null for session {SessionId}", AccountId);
                        continue;
                    }
                
                    await _sendSemaphore.WaitAsync();
                    await SendQueuedPacketAsync(packet);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Lost connection to session {SessionId}", AccountId);
                    break;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to process packet");
                }
            }
        }
        catch (OperationCanceledException)
        {
            
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to process packets");
        }
    }
    
    protected virtual async Task SendQueuedPacketAsync(NetworkPacket packet)
    {
        switch (packet.Header.Protocol)
        {
            case NetworkProtocol.Tcp:
                await Connection.SendAsync(packet).ContinueWith(_ =>
                {
                    _sendSemaphore.Release();
                });
                break;
            case NetworkProtocol.Invalid:
            case NetworkProtocol.Udp:
            case NetworkProtocol.Both:
            default:
                throw new InvalidOperationException("Cannot send a packet with no protocol specified.");
        }
    }
}
