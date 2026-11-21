using Xunit;
using System.Security.Cryptography.X509Certificates;
using Avalon.Api.Identity.Authentication;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// The services an allocator resolves the <c>"game-workload-ca"</c> holder from: a CA PEM on disk, loaded the way identity
/// loads it, that a test may replace and reload.
/// </summary>
internal sealed class WorkloadCaServices : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("avalon-ca-").FullName;
    private readonly ServiceProvider _provider;

    public WorkloadCaServices()
    {
        Ca = TestCertificates.CreateCa("Avalon test CA");
        Holder = ReloadingCertificate.Load(Write(Ca), null, NullLogger.Instance, new FakeTimeProvider(), TimeSpan.FromMinutes(2));
        _provider = new ServiceCollection().AddKeyedSingleton(GameWorkloadHosting.CaKey, Holder).BuildServiceProvider();
    }

    public X509Certificate2 Ca { get; }
    public ReloadingCertificate Holder { get; }
    public IServiceProvider Services => _provider;

    /// <summary>Replaces the CA file with <paramref name="next"/> and reloads, as the kubelet and the poll timer do at renewal.</summary>
    public void Swap(X509Certificate2 next)
    {
        Write(next);
        Assert.True(Holder.TryReload());
    }

    private string Write(X509Certificate2 ca)
    {
        string path = Path.Combine(_directory, "ca.crt");
        File.WriteAllText(path, ca.ExportCertificatePem());
        return path;
    }

    public void Dispose()
    {
        Holder.Dispose();
        _provider.Dispose();
        Directory.Delete(_directory, true);
    }
}
