using System.Collections.Concurrent;
using System.Net;
using Avalon.Crashes.Worker.Processing;

namespace Avalon.Crashes.Worker.Symbols;

public enum PublicSymbolStatus { Available, Ineligible, NotFound, UnsupportedCompressed, Invalid, Transient }
public sealed record PublicSymbolResult(PublicSymbolStatus Status, string? ImagePath = null, string? PdbPath = null);

/// <summary>Version-one system allowlist. CAB/SZDD compression is unsupported: cache a miss for 24 hours, without expansion.</summary>
public sealed class PublicSymbolClient : IDisposable
{
    private static readonly HashSet<string> s_modules = new(StringComparer.OrdinalIgnoreCase)
    { "ntdll.dll", "kernel32.dll", "kernelbase.dll", "ucrtbase.dll", "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" };
    private readonly HttpClient _client;
    private readonly CrashSymbolOptions _options;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (PublicSymbolStatus Status, DateTimeOffset Until)> _negative = new(StringComparer.Ordinal);

    public PublicSymbolClient(CrashSymbolOptions options, TimeProvider? timeProvider = null)
        : this(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, Credentials = null, AutomaticDecompression = DecompressionMethods.None }, options, timeProvider) { }

    /// <summary>Custom handlers must not redirect, decompress, attach credentials or retry outside the passed cancellation token.</summary>
    public PublicSymbolClient(HttpMessageHandler handler, CrashSymbolOptions options, TimeProvider? timeProvider = null)
    {
        options.Validate();
        if (handler is HttpClientHandler http)
        {
            http.AllowAutoRedirect = false;
            http.UseCookies = false;
            http.Credentials = null;
            http.UseDefaultCredentials = false;
            http.AutomaticDecompression = DecompressionMethods.None;
        }
        if (handler is SocketsHttpHandler sockets)
        {
            sockets.AllowAutoRedirect = false;
            sockets.UseCookies = false;
            sockets.Credentials = null;
            sockets.AutomaticDecompression = DecompressionMethods.None;
        }
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
    }

    public static bool IsEligible(ModuleIdentity identity) => SymbolFiles.Eligible(identity) && s_modules.Contains(identity.Name)
        && string.Equals(identity.PdbName, Path.GetFileNameWithoutExtension(identity.Name) + ".pdb", StringComparison.OrdinalIgnoreCase);

    public async Task<PublicSymbolResult> FetchAsync(ModuleIdentity identity, CrashAttemptWorkspace workspace, SymbolDownloadBudget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        budget.RestrictTo(_options.DownloadBytes);
        if (!_options.PublicEnabled || !IsEligible(identity))
            return new(PublicSymbolStatus.Ineligible);
        string key = SymbolFiles.LockIdentity(identity);
        if (_negative.TryGetValue(key, out (PublicSymbolStatus Status, DateTimeOffset Until) cached) && cached.Until > _time.GetUtcNow())
            return new(cached.Status);
        string image = Path.Combine(workspace.DirectoryPath, identity.Name);
        string pdb = Path.Combine(workspace.DirectoryPath, identity.PdbName!);
        PublicSymbolStatus status;
        bool success = false;
        try
        {
            status = await DownloadAsync(identity.Name, identity.ImageKey, workspace, budget, ct);
            if (status == PublicSymbolStatus.Available)
                status = await DownloadAsync(identity.PdbName!, identity.PdbKey!, workspace, budget, ct);
            if (status == PublicSymbolStatus.Available && !await SymbolIdentityValidator.ValidatePairAsync(identity, image, pdb, ct))
                status = PublicSymbolStatus.Invalid;
            if (status == PublicSymbolStatus.Available)
            {
                success = true;
                return new(status, image, pdb);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or CrashSymbolException)
        {
            ct.ThrowIfCancellationRequested();
            status = PublicSymbolStatus.Transient;
        }
        finally
        {
            if (!success)
            {
                File.Delete(image);
                File.Delete(pdb);
            }
        }
        if (_negative.Count >= 4096)
        {
            foreach (string old in _negative.OrderBy(x => x.Value.Until).Take(512).Select(x => x.Key))
                _negative.TryRemove(old, out _);
        }
        _negative[key] = (status, _time.GetUtcNow().Add(status == PublicSymbolStatus.Transient ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(24)));
        return new(status);
    }

    private async Task<PublicSymbolStatus> DownloadAsync(string name, string id, CrashAttemptWorkspace workspace, SymbolDownloadBudget budget, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _time);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        Uri uri = new(_options.PublicOrigin.AbsoluteUri.TrimEnd('/') + $"/{name}/{id}/{name}");
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                Uri? location = response.Headers.Location;
                if (location is null || redirect == 3)
                    return PublicSymbolStatus.Invalid;
                Uri next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (!CrashSymbolOptions.IsApprovedPublicAuthority(next) || next.UserInfo.Length > 0 || next.Fragment.Length > 0
                    || !new[] { _options.PublicOrigin }.Concat(_options.RedirectOrigins).Any(o => o.GetLeftPart(UriPartial.Authority) == next.GetLeftPart(UriPartial.Authority)))
                {
                    return PublicSymbolStatus.Invalid;
                }
                uri = next;
                continue;
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
                return PublicSymbolStatus.NotFound;
            if (!response.IsSuccessStatusCode)
                return PublicSymbolStatus.Transient;
            if (response.Content.Headers.ContentEncoding.Count != 0)
                return PublicSymbolStatus.UnsupportedCompressed;
            long cap = Math.Min(_options.ArtifactBytes, budget.RemainingBytes);
            if (cap <= 0 || response.Content.Headers.ContentLength > cap)
                throw new CrashSymbolException(CrashFailureCode.ResourceLimit);
            await using Stream body = await response.Content.ReadAsStreamAsync(operation.Token);
            byte[] prefix = new byte[4];
            int count = await body.ReadAtLeastAsync(prefix, 4, false, operation.Token);
            budget.Charge(count);
            if (prefix.AsSpan(0, count).SequenceEqual("MSCF"u8) || prefix.AsSpan(0, count).SequenceEqual("SZDD"u8))
                return PublicSymbolStatus.UnsupportedCompressed;
            if (count > cap)
                throw new CrashSymbolException(CrashFailureCode.ResourceLimit);
            await using (Stream output = await workspace.CreateFileAsync(name, cap, operation.Token))
            {
                await output.WriteAsync(prefix.AsMemory(0, count), operation.Token);
                await SymbolFiles.CopyAsync(body, output, cap - count, budget, operation.Token);
            }
            operation.Token.ThrowIfCancellationRequested();
            return PublicSymbolStatus.Available;
        }
        return PublicSymbolStatus.Invalid;
    }

    public void Dispose() => _client.Dispose();
}
