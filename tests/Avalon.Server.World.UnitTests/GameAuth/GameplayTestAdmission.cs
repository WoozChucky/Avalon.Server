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
    private static readonly Lazy<WorldTlsTransport> s_transport = new(BuildTransport);

    /// <summary>
    /// The one transport the whole test process shares (a context persists its CA in the Windows CA store, so each build
    /// costs the machine something). Its holders live until the process exits, where they are disposed.
    /// </summary>
    public static WorldTlsTransport TlsTransport() => s_transport.Value;

    private static WorldTlsTransport BuildTransport()
    {
        using X509Certificate2 ca = TestCertificates.CreateCa(TestCertificates.UniqueName("avalon-ca"));
        using X509Certificate2 leaf = TestCertificates.CreateLeaf(ca, "localhost", serverAuth: true);
        string directory = Directory.CreateTempSubdirectory("world-tls").FullName;
        try
        {
            (string certPath, string keyPath) = TestCertificates.WritePem(leaf, directory, "tls");
            string caPath = Path.Combine(directory, "ca.crt");
            File.WriteAllText(caPath, ca.ExportCertificatePem());
            // Never polled: the files are read once, here, and deleted below.
            var leafHolder = ReloadingCertificate.Load(certPath, keyPath, NullLogger.Instance, TimeProvider.System, Timeout.InfiniteTimeSpan);
            var caHolder = ReloadingCertificate.Load(caPath, null, NullLogger.Instance, TimeProvider.System, Timeout.InfiniteTimeSpan);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { leafHolder.Dispose(); caHolder.Dispose(); };
            return new(leafHolder, caHolder);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
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
