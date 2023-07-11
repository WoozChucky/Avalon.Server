namespace Avalon.Network.Packets.Internal.Exceptions;

public class PacketHandlerException : Exception
{
    public PacketHandlerException(string? message) : base(message)
    {
    }

    public PacketHandlerException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
