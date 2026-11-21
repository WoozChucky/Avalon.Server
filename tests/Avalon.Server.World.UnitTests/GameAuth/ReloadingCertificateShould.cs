using System.Security.Cryptography.X509Certificates;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class ReloadingCertificateShould : IDisposable
{
    private static readonly TimeSpan s_poll = TimeSpan.FromMinutes(2);
    private readonly string _directory = Directory.CreateTempSubdirectory("avalon-reload").FullName;
    private readonly X509Certificate2 _ca = TestCertificates.CreateCa("avalon-ca");

    public void Dispose()
    {
        _ca.Dispose();
        try { Directory.Delete(_directory, true); } catch (IOException) { }
    }

    private X509Certificate2 Leaf() => TestCertificates.CreateLeaf(_ca, "world", serverAuth: true);

    [Fact]
    public void Swap_to_a_renewed_pair_and_keep_serving_the_old_one_until_then()
    {
        using X509Certificate2 first = Leaf();
        (string cert, string key) = TestCertificates.WritePem(first, _directory, "tls");
        var clock = new FakeTimeProvider();
        using var reloading = ReloadingCertificate.Load(cert, key, NullLogger.Instance, clock, s_poll);
        Assert.Equal(first.Thumbprint, reloading.Current.Thumbprint);
        Assert.True(reloading.Current.HasPrivateKey);

        using X509Certificate2 renewed = Leaf();
        TestCertificates.WritePem(renewed, _directory, "tls");
        Assert.Equal(first.Thumbprint, reloading.Current.Thumbprint);
        clock.Advance(s_poll);
        Assert.Equal(renewed.Thumbprint, reloading.Current.Thumbprint);
        Assert.False(reloading.TryReload());
    }

    [Theory]
    [InlineData("half-written")]
    [InlineData("key-of-another-cert")]
    [InlineData("missing")]
    public void Keep_the_current_certificate_when_the_new_files_do_not_load(string damage)
    {
        using X509Certificate2 first = Leaf();
        (string cert, string key) = TestCertificates.WritePem(first, _directory, "tls");
        using var reloading = ReloadingCertificate.Load(cert, key, NullLogger.Instance, new FakeTimeProvider(), s_poll);

        using X509Certificate2 renewed = Leaf();
        TestCertificates.WritePem(renewed, _directory, "tls");
        switch (damage)
        {
            case "half-written": File.WriteAllText(cert, File.ReadAllText(cert)[..40]); break;
            case "key-of-another-cert":
                using (X509Certificate2 other = Leaf())
                    File.Copy(TestCertificates.WritePem(other, _directory, "other").KeyPath, key, true);
                break;
            default: File.Delete(cert); break;
        }
        Assert.False(reloading.TryReload());
        Assert.Equal(first.Thumbprint, reloading.Current.Thumbprint);
    }

    [Fact]
    public void Refuse_to_start_without_a_loadable_pair()
    {
        string missing = Path.Combine(_directory, "none.crt");
        Assert.ThrowsAny<Exception>(() => ReloadingCertificate.Load(missing, Path.ChangeExtension(missing, ".key"), NullLogger.Instance, new FakeTimeProvider(), s_poll));
    }
}
