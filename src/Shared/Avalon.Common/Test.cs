using System.Text;

namespace Avalon.Common;

public class Test
{

    public static void SerelizeByte(byte[] buffer, byte b, in int offset = 0)
    {
        buffer[offset] = b;
    }

    public static void SerializeUInt16(byte[] buffer, ushort s, in int offset = 0)
    {
        buffer[offset] = (byte)(s >> 0);
        buffer[offset + 1] = (byte)(s >> 8);
    }

    public static void SerializeInt16(byte[] buffer, short s, in int offset = 0)
    {
        buffer[offset] = (byte)(s >> 0);
        buffer[offset + 1] = (byte)(s >> 8);
    }

    public static void SerializeUInt32(byte[] buffer, uint i, in int offset = 0)
    {
        buffer[offset] = (byte)(i >> 0);
        buffer[offset + 1] = (byte)(i >> 8);
        buffer[offset + 2] = (byte)(i >> 16);
        buffer[offset + 3] = (byte)(i >> 24);
    }

    public static void SerializeInt32(byte[] buffer, int i, in int offset = 0)
    {
        buffer[offset] = (byte)(i >> 0);
        buffer[offset + 1] = (byte)(i >> 8);
        buffer[offset + 2] = (byte)(i >> 16);
        buffer[offset + 3] = (byte)(i >> 24);
    }

    public static void SerializeUInt64(byte[] buffer, ulong l, in int offset = 0)
    {
        buffer[offset] = (byte)(l >> 0);
        buffer[offset + 1] = (byte)(l >> 8);
        buffer[offset + 2] = (byte)(l >> 16);
        buffer[offset + 3] = (byte)(l >> 24);
        buffer[offset + 4] = (byte)(l >> 32);
        buffer[offset + 5] = (byte)(l >> 40);
        buffer[offset + 6] = (byte)(l >> 48);
        buffer[offset + 7] = (byte)(l >> 56);
    }

    public static void SerializeInt64(byte[] buffer, long l, in int offset = 0)
    {
        buffer[offset] = (byte)(l >> 0);
        buffer[offset + 1] = (byte)(l >> 8);
        buffer[offset + 2] = (byte)(l >> 16);
        buffer[offset + 3] = (byte)(l >> 24);
        buffer[offset + 4] = (byte)(l >> 32);
        buffer[offset + 5] = (byte)(l >> 40);
        buffer[offset + 6] = (byte)(l >> 48);
        buffer[offset + 7] = (byte)(l >> 56);
    }

    public static void SerializeFloat(byte[] buffer, float f, in int offset = 0)
    {
        var bytes = BitConverter.GetBytes(f);
        buffer[offset] = bytes[0];
        buffer[offset + 1] = bytes[1];
        buffer[offset + 2] = bytes[2];
        buffer[offset + 3] = bytes[3];
    }

    public static void SerializeDouble(byte[] buffer, double d, in int offset = 0)
    {
        var bytes = BitConverter.GetBytes(d);
        buffer[offset] = bytes[0];
        buffer[offset + 1] = bytes[1];
        buffer[offset + 2] = bytes[2];
        buffer[offset + 3] = bytes[3];
        buffer[offset + 4] = bytes[4];
        buffer[offset + 5] = bytes[5];
        buffer[offset + 6] = bytes[6];
        buffer[offset + 7] = bytes[7];
    }

    public static void SerializeString(byte[] buffer, string s, in int offset = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        SerializeUInt16(buffer, (ushort)bytes.Length, offset);
        Buffer.BlockCopy(bytes, 0, buffer, offset + 2, bytes.Length);
    }

    public static byte DeserializeByte(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        return buffer[offset];
    }

    public static ushort DeserializeUInt16(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    public static short DeserializeInt16(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        return (short)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    public static uint DeserializeUInt32(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
    }

    public static int DeserializeInt32(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        return (int)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
    }

    public static ulong DeserializeUInt64(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        return (ulong)buffer[offset]
                   | ((ulong)buffer[offset + 1] << 8)
                   | ((ulong)buffer[offset + 2] << 16)
                   | ((ulong)buffer[offset + 3] << 24)
                   | ((ulong)buffer[offset + 4] << 32)
                   | ((ulong)buffer[offset + 5] << 40)
                   | ((ulong)buffer[offset + 6] << 48)
                   | ((ulong)buffer[offset + 7] << 56);
    }

    public static long DeserializeInt64(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        return (long)buffer[offset]
                   | ((long)buffer[offset + 1] << 8)
                   | ((long)buffer[offset + 2] << 16)
                   | ((long)buffer[offset + 3] << 24)
                   | ((long)buffer[offset + 4] << 32)
                   | ((long)buffer[offset + 5] << 40)
                   | ((long)buffer[offset + 6] << 48)
                   | ((long)buffer[offset + 7] << 56);
    }

    public static float DeserializeFloat(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        var bytes = new byte[4];
        bytes[0] = buffer[offset];
        bytes[1] = buffer[offset + 1];
        bytes[2] = buffer[offset + 2];
        bytes[3] = buffer[offset + 3];
        return BitConverter.ToSingle(bytes, 0);
    }

    public static double DeserializeDouble(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        var bytes = new byte[8];
        bytes[0] = buffer[offset];
        bytes[1] = buffer[offset + 1];
        bytes[2] = buffer[offset + 2];
        bytes[3] = buffer[offset + 3];
        bytes[4] = buffer[offset + 4];
        bytes[5] = buffer[offset + 5];
        bytes[6] = buffer[offset + 6];
        bytes[7] = buffer[offset + 7];
        return BitConverter.ToDouble(bytes, 0);
    }

    public static string DeserializeString(ReadOnlySpan<byte> buffer, in int offset = 0)
    {
        var length = DeserializeUInt16(buffer, offset);
        return Encoding.UTF8.GetString(buffer.Slice(offset + 2, length));
    }

    public static void SerializePacket(byte[] buffer, Packet packet, int offset = 0)
    {
        SerializeHeader(buffer, packet.Header, offset);
        Buffer.BlockCopy(packet.Payload, 0, buffer, offset + 8, packet.Payload.Length);
    }

    public static void SerializeHeader(byte[] buffer, PacketHeader header, int offset = 0)
    {
        SerializeUInt16(buffer, (ushort)header.Type, offset);
        SerializeUInt16(buffer, (ushort)header.Flags, offset + 2);
        SerializeUInt16(buffer, (ushort)header.Protocol, offset + 4);
        SerializeUInt16(buffer, header.Version, offset + 6);
    }

}

// Get serialized first, and then put into the Payload of the Packet class
public class HelloPacket
{
    public uint Age { get; set; }
    public bool Active { get; set; }
}

// Get serialized first, and then put into the Payload of the Packet class
public class OtherPacket
{
    public string Name { get; set; }
    public long Money { get; set; }
}

public class Packet
{
    public PacketHeader Header { get; set; }
    public byte[] Payload { get; set; } = [];

    public uint Size => (uint)(Header.Size + Payload.Length);
}

public class PacketHeader
{
    internal NetworkPacketTypeEx Type { get; set; }
    internal NetworkPacketFlags Flags { get; set; }
    internal NetworkProtocol Protocol { get; set; }
    internal ushort Version { get; set; }

    public uint Size => 2 + 2 + 2 + 2;
}

public enum NetworkPacketTypeEx : ushort
{
    // Add your packet types here
}

public enum NetworkPacketFlags : ushort
{
    // Add your packet flags here
}

public enum NetworkProtocol : ushort
{
    // Add your network protocols here
}
