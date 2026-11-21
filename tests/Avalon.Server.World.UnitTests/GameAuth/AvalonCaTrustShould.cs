using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.Testing;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class AvalonCaTrustShould
{
    [Fact]
    public void Accept_a_current_leaf_issued_by_the_ca()
    {
        using X509Certificate2 ca = TestCertificates.CreateCa("avalon-ca");
        using X509Certificate2 leaf = TestCertificates.CreateLeaf(ca, "world", serverAuth: true);
        Assert.True(AvalonCaTrust.Chains(leaf, ca, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("other-ca")]
    [InlineData("self-signed")]
    [InlineData("expired")]
    [InlineData("the-ca-itself")]
    public void Refuse_a_leaf_not_issued_by_the_ca_or_not_current(string kind)
    {
        using X509Certificate2 ca = TestCertificates.CreateCa("avalon-ca");
        // The impostor has the trusted CA's name, so refusal has to come from the signature, not the issuer name.
        using X509Certificate2 otherCa = TestCertificates.CreateCa("avalon-ca");
        using X509Certificate2 leaf = kind switch
        {
            "other-ca" => TestCertificates.CreateLeaf(otherCa, "world", serverAuth: true),
            "self-signed" => SelfSigned(),
            "expired" => TestCertificates.CreateLeaf(ca, "world", serverAuth: true, notAfter: DateTimeOffset.UtcNow.AddHours(-1)),
            _ => X509CertificateLoader.LoadCertificate(ca.RawData),
        };
        Assert.False(AvalonCaTrust.Chains(leaf, ca, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(new[] { "urn:avalon:server:world-ptr" }, "world-ptr")]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "urn:avalon:server:a", "urn:avalon:server:b" }, null)]
    [InlineData(new[] { "urn:other:world-ptr" }, null)]
    public void Read_the_server_id_from_exactly_one_avalon_uri(string[] uris, string? expected)
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=world", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("world");
        foreach (string uri in uris) names.AddUri(new Uri(uri));
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 leaf = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(expected, AvalonCaTrust.ServerIdOf(leaf));
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=world", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
