using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Hosting.Networking;

/// <summary>
/// What a server knows about a packet it receives: its type and, when it has one, the handler it
/// dispatches to. <see cref="PacketManager" /> builds every one once, at startup, and only reads them
/// afterwards, so connections dispatching at once share them without a lock (#866).
/// </summary>
public readonly struct PacketInfo
{
    /// <param name="packetType">The packet's type.</param>
    /// <param name="packetHandlerType">The handler it dispatches to, if any.</param>
    /// <param name="handlerFactory">
    /// Builds <paramref name="packetHandlerType" />; <see cref="PacketManager" /> passes the one it built at
    /// startup. Left out, it is built here, once per call.
    /// </param>
    public PacketInfo(Type packetType, Type? packetHandlerType = null, ObjectFactory? handlerFactory = null)
    {
        PacketType = packetType;
        PacketHandlerType = packetHandlerType;
        HandlerFactory = handlerFactory ?? (packetHandlerType is null ? null : ActivatorUtilities.CreateFactory(packetHandlerType, []));
    }

    public Type PacketType { get; }
    public Type? PacketHandlerType { get; }

    /// <summary>Builds the handler from a dispatch's scope; null when the packet has no handler.</summary>
    public ObjectFactory? HandlerFactory { get; }
}
