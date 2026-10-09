namespace Avalon.Common.Cryptography;

/// <summary>
/// Seals one serialized packet for the wire. Every packet's <c>Create</c> takes one; on a
/// connection it is <see cref="IAvalonCryptoSession.Encryptor" />, the session's delegate created once.
/// </summary>
public delegate byte[] EncryptFunc(ReadOnlySpan<byte> plaintext);
