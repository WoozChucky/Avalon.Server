namespace Avalon.Network.Packets.Serialization;

public delegate byte[] EncryptFunc(ReadOnlySpan<byte> plaintext);
