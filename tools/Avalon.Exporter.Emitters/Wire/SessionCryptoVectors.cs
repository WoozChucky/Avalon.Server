using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avalon.Common.Cryptography;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

namespace Avalon.Exporter;

/// <summary>One sealed packet in one direction of one exchange.</summary>
public sealed record CryptoPacket(string Direction, ulong Counter, byte[] Plaintext, byte[] Nonce, byte[] Ciphertext);

/// <summary>One frozen ECDH exchange and everything derived from it.</summary>
public sealed record CryptoExchange(
    string Name,
    byte[] ClientPrivateScalar,
    byte[] ClientPublicKeyDer,
    byte[] ServerPrivateScalar,
    byte[] ServerPublicKeyDer,
    byte[] SharedSecret,
    byte[] Salt,
    byte[] ClientToServer,
    byte[] ServerToClient,
    IReadOnlyList<CryptoPacket> Packets);

/// <summary>
/// Known-answer vectors for the v1 session crypto: two frozen ECDH exchanges, the keys derived
/// from each, and sealed packets in both directions.
/// </summary>
/// <remarks>
/// <para>
/// The client and the server derive the same two keys independently and never compare them, so a
/// derivation that disagrees fails as a packet that will not open — with nothing on the wire to
/// say which end is wrong. These vectors are what makes that a check either end can run offline,
/// against no server and no .NET.
/// </para>
/// <para>
/// The sealed packets come from real <see cref="AvalonCryptoSession"/> instances rather than from
/// a second copy of the algorithm, so the file cannot agree with a reimplementation that the
/// server does not run.
/// </para>
/// </remarks>
public static class SessionCryptoVectors
{
    public const string DirectoryName = "crypto";
    public const string FileName = "session-v1.txt";

    private const int ScalarSize = 32;

    /// <summary>The bytes a c2s packet carries, in counter order.</summary>
    private static readonly byte[][] ClientPlaintexts =
    [
        Sequence(32),                                    // handshake-shaped
        [],                                              // seals to a bare tag
        Encoding.ASCII.GetBytes("avalon/v1 client hello"),
    ];

    /// <summary>The bytes an s2c packet carries, in counter order.</summary>
    private static readonly byte[][] ServerPlaintexts =
    [
        Sequence(32),
        [0xff],
        Sequence(200),                                   // two varint-length bytes on the wire above it
    ];

    public static string Generate()
    {
        var builder = new StringBuilder();

        Header(builder);

        foreach (CryptoExchange exchange in Exchanges())
        {
            Render(builder, exchange);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads back what <see cref="Generate"/> wrote. The vectors are only a conformance artifact
    /// if something checks the checked-in bytes rather than regenerating them, so this is the half
    /// that makes the file load-bearing.
    /// </summary>
    public static IReadOnlyList<CryptoExchange> Parse(string content)
    {
        List<CryptoExchange> exchanges = [];
        Dictionary<string, byte[]> values = [];
        List<CryptoPacket> packets = [];
        string? exchangeName = null;

        string? packetDirection = null;
        ulong packetCounter = 0;
        Dictionary<string, byte[]> packetValues = [];

        string? pending = null;
        int pendingLength = 0;
        List<byte> pendingBytes = [];
        Dictionary<string, byte[]>? pendingSink = null;

        foreach (string raw in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = raw.TrimEnd();
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            switch (parts[0])
            {
                case "exchange":
                    FlushValue();
                    FlushPacket();
                    FlushExchange();
                    exchangeName = parts[1];
                    break;

                case "packet":
                    FlushValue();
                    FlushPacket();
                    packetDirection = parts[1];
                    packetCounter = ulong.Parse(parts[2], CultureInfo.InvariantCulture);
                    break;

                case "value":
                    FlushValue();
                    pending = parts[1];
                    pendingLength = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    pendingBytes = [];
                    pendingSink = packetDirection is null ? values : packetValues;
                    break;

                default:
                    // A hex continuation of the value above it. Anything else is a format error
                    // rather than something to skip, or a typo reads as a shorter vector.
                    if (pending is null)
                    {
                        throw new FormatException($"Unexpected line outside a value: {trimmed}");
                    }

                    foreach (string token in parts)
                    {
                        pendingBytes.Add(Convert.ToByte(token, 16));
                    }

                    break;
            }
        }

        FlushValue();
        FlushPacket();
        FlushExchange();

        return exchanges;

        void FlushValue()
        {
            if (pending is null) return;

            if (pendingBytes.Count != pendingLength)
            {
                throw new FormatException(
                    $"{pending} declares {pendingLength.ToString(CultureInfo.InvariantCulture)} bytes"
                    + $" and carries {pendingBytes.Count.ToString(CultureInfo.InvariantCulture)}.");
            }

            pendingSink![pending] = [.. pendingBytes];
            pending = null;
        }

        void FlushPacket()
        {
            if (packetDirection is null) return;

            packets.Add(new CryptoPacket(
                packetDirection,
                packetCounter,
                packetValues["plaintext"],
                packetValues["nonce"],
                packetValues["ciphertext"]));

            packetDirection = null;
            packetValues = [];
        }

        void FlushExchange()
        {
            if (exchangeName is null) return;

            exchanges.Add(new CryptoExchange(
                exchangeName,
                values["clientPrivateScalar"],
                values["clientPublicKeyDer"],
                values["serverPrivateScalar"],
                values["serverPublicKeyDer"],
                values["sharedSecret"],
                values["salt"],
                values["c2s"],
                values["s2c"],
                packets));

            exchangeName = null;
            values = [];
            packets = [];
        }
    }

    /// <summary>The frozen exchanges, in the order they are written.</summary>
    public static IReadOnlyList<CryptoExchange> Exchanges() =>
    [
        Build("ordinary", Scalar("client", 0), Scalar("server", 0)),
        Build("leading-zero-secret", Scalar("client", 1), ServerScalarWithShortSecret()),
    ];

    /// <summary>
    /// Everything one exchange produces, run through the production key agreement, the production
    /// derivation and two production sessions.
    /// </summary>
    private static CryptoExchange Build(string name, BigInteger clientScalar, BigInteger serverScalar)
    {
        AsymmetricCipherKeyPair client = KeyPair(clientScalar);
        AsymmetricCipherKeyPair server = KeyPair(serverScalar);

        byte[] clientDer = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(client));
        byte[] serverDer = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(server));

        byte[] secret = AsymmetricCipher.CalculateSharedSecret(
            client, AsymmetricCipher.GetPublicKeyFromBytes(serverDer));

        (byte[] clientToServer, byte[] serverToClient) = SessionKeys.Derive(secret, clientDer, serverDer);

        var clientSession = new AvalonCryptoSession(CryptoRole.Client, client);
        clientSession.Initialize(serverDer);

        var serverSession = new AvalonCryptoSession(CryptoRole.Server, server);
        serverSession.Initialize(clientDer);

        List<CryptoPacket> packets = [];

        for (int i = 0; i < ClientPlaintexts.Length; i++)
        {
            packets.Add(Seal("c2s", (ulong)i, ClientPlaintexts[i], clientSession));
        }

        for (int i = 0; i < ServerPlaintexts.Length; i++)
        {
            packets.Add(Seal("s2c", (ulong)i, ServerPlaintexts[i], serverSession));
        }

        return new CryptoExchange(
            name, Fixed(clientScalar), clientDer, Fixed(serverScalar), serverDer,
            secret, SessionKeys.Salt(clientDer, serverDer), clientToServer, serverToClient, packets);
    }

    private static CryptoPacket Seal(string direction, ulong counter, byte[] plaintext, IAvalonCryptoSession session)
    {
        byte[] sealedPacket = session.Encrypt(plaintext);

        return new CryptoPacket(
            direction,
            counter,
            plaintext,
            sealedPacket[..SessionKeys.NonceSize],
            sealedPacket[SessionKeys.NonceSize..]);
    }

    /// <summary>
    /// The first server scalar in the chain whose agreement with client scalar 1 has a zero top
    /// byte — the case that used to yield a 31-byte AES key, and the one a client that trims
    /// leading zeros still gets wrong.
    /// </summary>
    private static BigInteger ServerScalarWithShortSecret()
    {
        ECPrivateKeyParameters clientPrivate = (ECPrivateKeyParameters)KeyPair(Scalar("client", 1)).Private;

        for (int index = 0; index < 100_000; index++)
        {
            BigInteger candidate = Scalar("server-short", index);
            var agreement = new ECDHBasicAgreement();
            agreement.Init(clientPrivate);

            if (agreement.CalculateAgreement((ECPublicKeyParameters)KeyPair(candidate).Public)
                    .ToByteArrayUnsigned().Length < ScalarSize)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "No leading-zero x-coordinate in 100,000 exchanges, which at the expected 1-in-256 rate "
            + "is vanishingly unlikely — suspect the search rather than the curve.");
    }

    /// <summary>
    /// A private scalar, from SHA-256 over a label and an index.
    /// </summary>
    /// <remarks>
    /// Chosen this way rather than drawn from a generator so the file can be rebuilt from the
    /// strings in it alone. A BouncyCastle PRNG would tie a vendored artifact to a library
    /// version, and a literal would not say where it came from.
    /// </remarks>
    private static BigInteger Scalar(string label, int index)
    {
        X9ECParameters curve = CustomNamedCurves.GetByName("secp256r1");

        for (int salt = 0; salt < 1000; salt++)
        {
            byte[] digest = SHA256.HashData(Encoding.ASCII.GetBytes(
                $"avalon/v1 kat {label} {index.ToString(CultureInfo.InvariantCulture)}"
                + (salt == 0 ? string.Empty : "." + salt.ToString(CultureInfo.InvariantCulture))));

            var scalar = new BigInteger(1, digest);

            if (scalar.SignValue > 0 && scalar.CompareTo(curve.N) < 0)
            {
                return scalar;
            }
        }

        throw new InvalidOperationException($"No valid P-256 scalar for {label} {index}.");
    }

    private static AsymmetricCipherKeyPair KeyPair(BigInteger scalar)
    {
        // The named-curve domain, not a bare ECDomainParameters: it is what makes the exported
        // SubjectPublicKeyInfo carry the P-256 OID rather than explicit parameters, and the
        // explicit form is a different length, which the handshake's length check rejects.
        var domain = new ECNamedDomainParameters(
            SecObjectIdentifiers.SecP256r1, CustomNamedCurves.GetByName("secp256r1"));

        return new AsymmetricCipherKeyPair(
            new ECPublicKeyParameters("ECDH", domain.G.Multiply(scalar).Normalize(), domain),
            new ECPrivateKeyParameters("ECDH", scalar, domain));
    }

    /// <summary>The scalar at the curve's own width, leading zeros kept.</summary>
    private static byte[] Fixed(BigInteger scalar)
    {
        byte[] minimal = scalar.ToByteArrayUnsigned();
        var padded = new byte[ScalarSize];
        Buffer.BlockCopy(minimal, 0, padded, ScalarSize - minimal.Length, minimal.Length);
        return padded;
    }

    private static byte[] Sequence(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)i;
        return bytes;
    }

    private static void Header(StringBuilder builder)
    {
        builder.Append(
            """
            # Session crypto - known-answer vectors for the v1 key derivation.
            #
            # GENERATED FILE. Every value below is what the server's own crypto produces for the
            # private scalars shown, so a client is conformant when it reproduces them, and a change
            # in what the server derives arrives here as a diff.
            #
            #   Emitted by  tools/Avalon.Exporter
            #   Regenerate  dotnet run --project tools/Avalon.Exporter -- crypto
            #   Explained   docs/crypto-v1-derivation.md
            #
            # The two ends derive the same two keys independently and never compare them, so a
            # derivation that disagrees fails as a packet that will not open, with nothing on the
            # wire to say which end is wrong. Check against these before blaming a server.
            #
            #   curve   P-256 (secp256r1), ephemeral, one exchange per connection
            #   secret  the ECDH x-coordinate at a FIXED 32 bytes, leading zeros kept
            #   salt    clientPublicKeyDer || serverPublicKeyDer, that order at both ends
            #   c2s     HKDF-SHA256(ikm: secret, salt: salt, info: "avalon/v1 c2s", 32 bytes)
            #   s2c     HKDF-SHA256(ikm: secret, salt: salt, info: "avalon/v1 s2c", 32 bytes)
            #   aead    AES-256-GCM, 16-byte tag; the client seals with c2s, the server with s2c
            #   nonce   a 96-bit big-endian counter from 0, per direction, sent with every packet
            #   wire    [12-byte nonce][ciphertext][16-byte tag]
            #
            # The info strings are ASCII with no terminator: 61 76 61 6c 6f 6e 2f 76 31 20 63 32 73
            # and 61 76 61 6c 6f 6e 2f 76 31 20 73 32 63.
            #
            # A scalar here is SHA-256 over "avalon/v1 kat <label> <index>", so the file can be
            # rebuilt from the strings in it. A client needs only the bytes.
            #
            # Format: "value <name> <length>" or "packet <direction> <counter>", then the bytes as
            # lowercase hex, sixteen per line, indented under the line that declares them. Lines
            # beginning "#" are commentary.
            #
            # The second exchange is not a duplicate. Its shared x-coordinate has a zero top byte -
            # about one exchange in 256 does - and an implementation that encodes the secret
            # minimally derives a different key for it and for nothing else.


            """);
    }

    private static void Render(StringBuilder builder, CryptoExchange exchange)
    {
        builder.Append("exchange ").Append(exchange.Name).Append('\n').Append('\n');

        Value(builder, "  ", "clientPrivateScalar", exchange.ClientPrivateScalar);
        Value(builder, "  ", "clientPublicKeyDer", exchange.ClientPublicKeyDer);
        Value(builder, "  ", "serverPrivateScalar", exchange.ServerPrivateScalar);
        Value(builder, "  ", "serverPublicKeyDer", exchange.ServerPublicKeyDer);
        Value(builder, "  ", "sharedSecret", exchange.SharedSecret);
        Value(builder, "  ", "salt", exchange.Salt);
        Value(builder, "  ", "c2s", exchange.ClientToServer);
        Value(builder, "  ", "s2c", exchange.ServerToClient);

        foreach (CryptoPacket packet in exchange.Packets)
        {
            builder.Append('\n')
                .Append("  packet ").Append(packet.Direction).Append(' ')
                .Append(packet.Counter.ToString(CultureInfo.InvariantCulture)).Append('\n');

            Value(builder, "    ", "plaintext", packet.Plaintext);
            Value(builder, "    ", "nonce", packet.Nonce);
            Value(builder, "    ", "ciphertext", packet.Ciphertext);
        }

        builder.Append('\n');
    }

    private static void Value(StringBuilder builder, string indent, string name, byte[] bytes)
    {
        builder.Append(indent).Append("value ").Append(name).Append(' ')
            .Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');

        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            builder.Append(indent).Append("  ");

            for (int i = offset; i < Math.Min(offset + 16, bytes.Length); i++)
            {
                if (i > offset) builder.Append(' ');
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            builder.Append('\n');
        }
    }
}
