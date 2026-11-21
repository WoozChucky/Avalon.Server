using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.GameAuth;
using Avalon.World.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.GameAuth;

internal static class GameplayTestAdmission
{
    public static WorldTlsTransport TlsTransport()
    {
        using X509Certificate2 ca = TestCertificates.CreateCa("avalon-ca");
        using X509Certificate2 leaf = TestCertificates.CreateLeaf(ca, "localhost", serverAuth: true);
        string directory = Directory.CreateTempSubdirectory("world-tls").FullName;
        (string certPath, string keyPath) = TestCertificates.WritePem(leaf, directory, "tls");
        string caPath = Path.Combine(directory, "ca.crt");
        File.WriteAllText(caPath, ca.ExportCertificatePem());
        // Never polled: the files are read once and the test host is short-lived.
        return new(ReloadingCertificate.Load(certPath, keyPath, NullLogger.Instance, TimeProvider.System, Timeout.InfiniteTimeSpan),
            ReloadingCertificate.Load(caPath, null, NullLogger.Instance, TimeProvider.System, Timeout.InfiniteTimeSpan));
    }
    public static GameSessionLease Admit(Avalon.World.WorldConnection connection, AccountId? account = null, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System; account ??= connection.AccountId ?? new AccountId(42);
        typeof(Avalon.World.WorldConnection).GetField("_tlsAuthenticated", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(connection, true);
        GameSessionLease lease = GameSessionLease.TryCreate(new()
        {
            State = "active",
            AccountId = account.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            GameSessionId = Guid.NewGuid().ToString("D"),
            GameContextId = Guid.NewGuid().ToString("D"),
            FencingToken = "1",
            ServerId = "world-one",
            WorldId = 1,
            AccessLevel = (ushort)connection.AccessLevel,
            CredentialsVersion = 0,
            SessionEpoch = "0",
            LeaseUntil = clock.GetUtcNow().UtcDateTime.AddSeconds(44),
            AuthorizationUntil = clock.GetUtcNow().UtcDateTime.AddMinutes(5)
        }, "world-one", 1, clock)!;
        connection.PublishAdmission(lease); connection.AcceptProtocol();
        return lease;
    }
}
