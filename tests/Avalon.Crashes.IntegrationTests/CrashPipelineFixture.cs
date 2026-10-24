using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Avalon.Api.Identity;
using Avalon.Api.Identity.Crashes;
using Avalon.Api.Testing;
using Avalon.Common.Accounts;
using Avalon.Crashes.IntegrationTests.Storage;
using Avalon.Crashes.Validation;
using Avalon.Crashes.Worker.Storage;
using Avalon.Database.Crashes;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Crashes.IntegrationTests;

internal sealed class CrashPipelineFixture : IAsyncDisposable
{
    public const string LogCanary = "vertical-private-log-https://private.invalid/?signature=never-export";
    public const string SqlCanary = "vertical-private-sql-secret";
    private readonly List<string> _keys = [];
    private readonly List<string> _originals = [];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vertical-" + Guid.NewGuid().ToString("N"));
    private AmazonS3Client _admin = null!;
    private Process? _worker;
    private Task<string>? _stdout;
    private Task<string>? _stderr;
    public VerticalDatabase Database { get; } = new();
    public ApiTestHost Api { get; private set; } = null!;
    public StoredCrashObject Source { get; private set; } = null!;
    public string ServiceName { get; } = "vertical-" + Guid.NewGuid().ToString("N");
    public string? LastOutput { get; private set; }

    public async Task InitializeAsync()
    {
        Assert.True(OperatingSystem.IsLinux());
        Assert.True(File.Exists("/app/Avalon.Crashes.Worker.dll"));
        Directory.CreateDirectory(_root);
        await Database.InitializeAsync();
        _admin = GarageClients.Create();
        string pair = Environment.GetEnvironmentVariable("AVALON_CRASHES_PRIVATE_PAIR")!;
        byte[] dump = await ReadDumpAsync();
        RuntimeIdentity identity = new MinidumpIdentityReader().Read(dump).Runtime;
        foreach ((string name, string id) in new[] { ("runtime.exe", identity.ImageKey), ("runtime.pdb", identity.PdbKey) })
        {
            _originals.Add($"{name}/{id}/{name}");
            await _admin.PutObjectAsync(new PutObjectRequest
            {
                BucketName = "symbols",
                Key = $"{name}/{id}/{name}",
                FilePath = Path.Combine(pair, name)
            });
        }
        Api = await ApiTestHost.StartAsync([IdentityApi.Service], new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?>
            {
                ["Application:Crashes:Enabled"] = "true",
                ["Database:Crashes:ConnectionString"] = Database.Identity
            }
        });
        Domain.Auth.Account account = ApiTestHost.MakeAccount(AccountAccessLevel.Admin);
        Api.AccountNowIs(account);
        Api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTestHost.Mint(account));
        Assert.True(await Api.Services.GetRequiredService<ICrashSchemaReadiness>().IsReadyAsync(default));
    }

    public async Task RequireRemoteCacheAsync()
    {
        Assert.Null(_worker);
        Directory.Delete(Path.Combine(_root, "cache"), recursive: true);
        foreach (string key in _originals) await _admin.DeleteObjectAsync("symbols", key);
    }

    public async Task<StoredCrashObject> UploadAsync(bool brokenThreads = false, bool nulText = false)
    {
        byte[] dump = await ReadDumpAsync();
        if (nulText)
        {
            // Modify only this in-memory copy. SystemInfo CSD is a length-prefixed UTF-16 string,
            // and the pinned walker emits its embedded NUL in system_info.os_ver.
            int directory = (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(12));
            int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(8));
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                int entry = directory + i * 12;
                if (BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(entry)) != 7) continue;
                int system = (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(entry + 8));
                byte[] text = Encoding.Unicode.GetBytes("before\0after");
                int append = dump.Length;
                Array.Resize(ref dump, append + 4 + text.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(dump.AsSpan(system + 24), (uint)append);
                BinaryPrimitives.WriteUInt32LittleEndian(dump.AsSpan(append), (uint)text.Length);
                text.CopyTo(dump, append + 4);
                changed = true;
                break;
            }
            Assert.True(changed);
            _ = new MinidumpIdentityReader().Read(dump);
        }
        if (brokenThreads)
        {
            int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(8));
            int directory = (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(12));
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                Span<byte> entry = dump.AsSpan(directory + i * 12, 12);
                if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != 3) continue;
                // Keep validated module identity, but make the real walker reject a missing thread list.
                BinaryPrimitives.WriteUInt32LittleEndian(entry, 0x47670001);
                changed = true;
            }
            Assert.True(changed);
            _ = new MinidumpIdentityReader().Read(dump);
        }
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(zip, "dump.dmp", dump);
            await WriteEntryAsync(zip, "meta.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = 1,
                exe = "runtime",
                version = "0.4.0",
                gitHash = "abcdef0123456789",
                exceptionCode = 0xc0000005u,
                exceptionAddress = "0x0",
                threadId = 23952,
                time = "2026-10-23T12:00:00Z"
            }));
            await WriteEntryAsync(zip, "log-tail.txt", Encoding.UTF8.GetBytes(nulText ? "before\0after literal\\u0000" : LogCanary));
        }
        Assert.True(bytes.Length <= CrashLimits.ArchiveBytes);
        bytes.Position = 0;
        string key = $"0.4.0/{DateTimeOffset.UtcNow:yyyy-MM-dd}/{Guid.NewGuid():N}.zip";
        _keys.Add(key);
        await _admin.PutObjectAsync(new PutObjectRequest { BucketName = "crashes", Key = key, InputStream = bytes, AutoCloseStream = false });
        using var store = new S3CrashObjectStore(_admin, new CrashStorageOptions { Bucket = "crashes" });
        Source = (await store.HeadAsync(key, default))!;
        return Source;
    }

    public void StartWorker()
    {
        Assert.Null(_worker);
        ProcessStartInfo start = WorkerStart("run", withStorage: true);
        _worker = Process.Start(start)!;
        _stdout = _worker.StandardOutput.ReadToEndAsync();
        _stderr = _worker.StandardError.ReadToEndAsync();
    }

    private ProcessStartInfo WorkerStart(string command, bool withStorage)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/app/Avalon.Crashes.Worker.dll");
        start.ArgumentList.Add(command);
        // Never pass test-admin or fixture credentials into the worker/cleanup process.
        foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("AVALON_CRASHES_", StringComparison.Ordinal)
            || k.StartsWith("Crashes__", StringComparison.Ordinal) || k.StartsWith("OTEL_", StringComparison.Ordinal)).ToArray())
        {
            start.Environment.Remove(key);
        }

        start.Environment["Database__Crashes__ConnectionString"] = Database.Owner;
        if (withStorage)
        {
            start.Environment["Crashes__Storage__Endpoint"] = GarageClients.Required("ENDPOINT");
            start.Environment["Crashes__Storage__AccessKeyId"] = GarageClients.Required("WORKER_ACCESS_KEY");
            start.Environment["Crashes__Storage__SecretAccessKey"] = GarageClients.Required("WORKER_SECRET_KEY");
            start.Environment["Crashes__Processing__ScratchRoot"] = Path.Combine(_root, "scratch");
            start.Environment["Crashes__Symbols__CacheRoot"] = Path.Combine(_root, "cache");
            start.Environment["Crashes__Symbols__PublicEnabled"] = "false";
            start.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4317";
            start.Environment["OTEL_SERVICE_NAME"] = ServiceName;
            start.Environment["OTEL_METRIC_EXPORT_INTERVAL"] = "1000";
            start.Environment["OTEL_BSP_SCHEDULE_DELAY"] = "100";
            start.Environment["OTEL_BLRP_SCHEDULE_DELAY"] = "100";
        }
        return start;
    }

    public async Task<CrashReport> WaitReportAsync(string key, Func<CrashReport, bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        while (!deadline.IsCancellationRequested)
        {
            Assert.False(_worker?.HasExited, "Shipped worker exited before report completion.");
            await using CrashesDbContext db = Database.Factory.CreateDbContext();
            CrashReport? report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.ObjectKey == key, deadline.Token);
            if (report is not null && predicate(report)) return report;
            if (report?.Status is CrashReportStatus.Rejected or CrashReportStatus.Failed)
                Assert.Fail("Unexpected terminal state: " + report.Status + "/" + report.ReasonCode);
            await Task.Delay(200, deadline.Token);
        }
        throw new TimeoutException("Report did not reach requested state.");
    }

    public async Task StopWorkerAsync()
    {
        if (_worker is null) return;
        if (!_worker.HasExited)
        {
            using Process signal = Process.Start("/bin/kill", ["-TERM", _worker.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)])!;
            await signal.WaitForExitAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await _worker.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { _worker.Kill(entireProcessTree: true); await _worker.WaitForExitAsync(); throw; }
        }
        LastOutput = await _stdout! + await _stderr!;
        Assert.Equal(0, _worker.ExitCode);
        _worker.Dispose();
        _worker = null;
    }

    public async Task CleanupAsync()
    {
        using Process cleanup = Process.Start(WorkerStart("cleanup", withStorage: false))!;
        Task<string> output = cleanup.StandardOutput.ReadToEndAsync();
        Task<string> error = cleanup.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await cleanup.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, cleanup.ExitCode);
        Assert.Contains("Deleted", await output);
        Assert.Empty(await error);
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        using HttpResponseMessage response = await Api.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    public async Task<JsonElement> PatchAsync(Guid group, long revision, string fields, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using HttpResponseMessage response = await Api.Client.PatchAsync($"/admin/crashes/groups/{group}",
            new StringContent($"{{\"triageRevision\":{revision},{fields}}}", Encoding.UTF8, "application/json"));
        Assert.Equal(expected, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static Task<byte[]> ReadDumpAsync() => File.ReadAllBytesAsync(Path.Combine(
        Environment.GetEnvironmentVariable("AVALON_CRASHES_PRIVATE_FIXTURE") ?? throw new InvalidOperationException("Protected fixture required."), "dump.dmp"));
    private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] bytes)
    {
        await using Stream entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await entry.WriteAsync(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopWorkerAsync(); }
        finally
        {
            if (Api is not null) await Api.DisposeAsync();
            if (_admin is not null)
            {
                foreach (string key in _keys) await _admin.DeleteObjectAsync("crashes", key);
                _admin.Dispose();
            }
            await Database.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
