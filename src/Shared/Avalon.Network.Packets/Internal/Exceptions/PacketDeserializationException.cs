namespace Avalon.Network.Packets.Internal.Exceptions;

public class PacketDeserializationException : Exception
{
    public PacketDeserializationException(string? message) : base(message)
    {
    }

    public PacketDeserializationException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
