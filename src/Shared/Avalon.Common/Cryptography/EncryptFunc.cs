namespace Avalon.Common.Cryptography;

/// <summary>
/// Seals one serialized client-to-server packet for the wire: each <c>C*Packet.Create</c> takes one, from the session's
/// <c>Encryptor</c>, a delegate created once. Server packets are sealed as they are framed (<c>SealInto</c>), not here.
/// </summary>
public delegate byte[] EncryptFunc(ReadOnlySpan<byte> plaintext);
