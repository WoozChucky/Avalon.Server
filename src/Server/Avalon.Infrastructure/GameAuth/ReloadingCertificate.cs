using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Avalon.Infrastructure.GameAuth;

public static class ReloadingCertificateDefaults
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
}

/// <summary>
/// A PEM certificate (with its key, or a CA alone) that re-reads its files on a timer, because the kubelet rewrites
/// cert-manager's Secret files in place at renewal. A renewal that does not load keeps the previous certificate.
/// </summary>
public sealed partial class ReloadingCertificate : IDisposable
{
    private readonly string _certificatePath;
    private readonly string? _keyPath;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly ITimer _timer;
    private X509Certificate2 _current;
    private X509Certificate2? _previous;
    private X509Certificate2? _older;
    private bool _disposed;

    private ReloadingCertificate(string certificatePath, string? keyPath, ILogger logger, X509Certificate2 first, TimeProvider clock, TimeSpan pollInterval)
    {
        _certificatePath = certificatePath;
        _keyPath = keyPath;
        _logger = logger;
        _current = first;
        _timer = clock.CreateTimer(_ => TryReload(), null, pollInterval, pollInterval);
    }

    /// <summary>Loads the first certificate, throwing if it cannot be read (startup refusal); <paramref name="keyPath"/> is null for a CA.</summary>
    public static ReloadingCertificate Load(string certificatePath, string? keyPath, ILogger logger, TimeProvider clock, TimeSpan pollInterval)
        => new(certificatePath, keyPath, logger, Read(certificatePath, keyPath), clock, pollInterval);

    public X509Certificate2 Current => Volatile.Read(ref _current);

    public bool TryReload()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            try
            {
                X509Certificate2 candidate = Read(_certificatePath, _keyPath);
                if (candidate.RawData.AsSpan().SequenceEqual(_current.RawData))
                {
                    candidate.Dispose();
                    return false;
                }
                // In-flight handshakes may still hold the replaced certificate, so only the one from two swaps ago goes.
                _older?.Dispose();
                _older = _previous;
                _previous = _current;
                Volatile.Write(ref _current, candidate);
                LogReloaded(_logger, _certificatePath);
                return true;
            }
            catch (Exception ex)
            {
                LogReloadFailed(_logger, ex, _certificatePath);
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            _current.Dispose();
            _previous?.Dispose();
            _older?.Dispose();
        }
    }

    private static X509Certificate2 Read(string certificatePath, string? keyPath)
    {
        // Both files are read into memory first and parsed from the bytes: a kubelet swap between the two reads then
        // shows up as a mismatched pair (refused) rather than being served.
#pragma warning disable MA0045 // Synchronous by design: the first load is startup, the rest run on the timer thread.
        string certificatePem = File.ReadAllText(certificatePath);
        if (keyPath is null) return X509Certificate2.CreateFromPem(certificatePem);
        string keyPem = File.ReadAllText(keyPath);
#pragma warning restore MA0045
        using var pem = X509Certificate2.CreateFromPem(certificatePem, keyPem);
        // Re-imported through PKCS#12 so SslStream can use the key on every platform (Windows needs a persisted key).
        return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reloaded the renewed certificate {Path}")]
    private static partial void LogReloaded(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The certificate {Path} did not reload; keeping the previous one")]
    private static partial void LogReloadFailed(ILogger logger, Exception ex, string path);
}
