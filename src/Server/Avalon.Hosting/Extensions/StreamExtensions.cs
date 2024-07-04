using System.IO;
using System.Threading.Tasks;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Hosting.Extensions;

public static class StreamExtensions
{
    public static Task WriteAsync(this Stream stream, NetworkPacket packet)
    {
        Serializer.SerializeWithLengthPrefix(stream, packet, PrefixStyle.Base128);
        return Task.CompletedTask;
    }
}
