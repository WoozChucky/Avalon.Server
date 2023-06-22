using System.Reflection;

namespace Avalon.Network.Packets.Serialization;

public interface IPacketSerializer
{
    Task SerializeToNetwork<T>(Stream destination, T packet) where T : class;
    Task Serialize<T>(Stream destination, T packet) where T : class;
    void RegisterPacketSerializers(Assembly? assembly = null);
}
