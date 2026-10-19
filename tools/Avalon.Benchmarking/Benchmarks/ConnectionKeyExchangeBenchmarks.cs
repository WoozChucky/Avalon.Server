using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What a connection's key exchange costs (#878): <c>Construct</c> is what every world connection paid as it was
/// created, a P-256 key pair and a session around it (<see cref="ConnectionKeyExchange" />), and <c>ConstructAndKey</c>
/// adds what admission paid, keying the session from a client's public key (the agreement, the derivation and the two
/// ciphers). Since #878 a world that does not seal pays neither; one that seals pays both.
/// </summary>
[MemoryDiagnoser]
public class ConnectionKeyExchangeBenchmarks
{
    private byte[] _clientKey = null!;

    [GlobalSetup]
    public void Setup() => _clientKey = new CryptoManager().GetPublicKey();

    [Benchmark(Baseline = true)]
    public ConnectionKeyExchange Construct() => new();

    [Benchmark]
    public ConnectionKeyExchange ConstructAndKey()
    {
        var exchange = new ConnectionKeyExchange();
        exchange.Session.Initialize(_clientKey);
        return exchange;
    }
}
