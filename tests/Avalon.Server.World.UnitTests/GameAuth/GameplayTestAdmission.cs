using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Common.ValueObjects;
using Avalon.World.GameAuth;

namespace Avalon.Server.World.UnitTests.GameAuth;

internal static class GameplayTestAdmission
{
    public static WorldTlsTransport TlsTransport()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        return new(X509CertificateLoader.LoadPkcs12(temporary.Export(X509ContentType.Pfx), null));
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
