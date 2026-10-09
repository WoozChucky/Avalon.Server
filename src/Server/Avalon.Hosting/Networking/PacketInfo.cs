using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Hosting.Networking;

/// <summary>
/// What a server knows about a packet it receives: its type and, when it has one, the handler it
/// dispatches to. <see cref="PacketManager" /> builds every one once, at startup, and only reads them
/// afterwards, so connections dispatching at once share them without a lock (#866).
/// </summary>
public readonly struct PacketInfo
{
    public PacketInfo(Type packetType, Type? packetHandlerType = null)
    {
        PacketType = packetType;
        PacketHandlerType = packetHandlerType;
        HandlerFactory = packetHandlerType is null ? null : ActivatorUtilities.CreateFactory(packetHandlerType, []);
    }

    public Type PacketType { get; }
    public Type? PacketHandlerType { get; }

    /// <summary>Builds the handler from a dispatch's scope; null when the packet has no handler.</summary>
    public ObjectFactory? HandlerFactory { get; }
}
