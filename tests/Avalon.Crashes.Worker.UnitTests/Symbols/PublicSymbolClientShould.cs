using System.Net;
using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Symbols;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Symbols;

public sealed class PublicSymbolClientShould
{
    private static readonly ModuleIdentity s_module = SymbolIdentityValidatorShould.s_runtime with { Name = "ntdll.dll", PdbName = "ntdll.pdb" };

    [Theory]
    [InlineData("https://arbitrary.example/", false)]
    [InlineData("https://127.0.0.1/", false)]
    [InlineData("https://msdl.microsoft.com/other-path/", false)]
    [InlineData("https://arbitrary.example/", true)]
    [InlineData("https://msdl.microsoft.com:8443/", true)]
    public void Should_reject_configuration_outside_code_approved_origins(string origin, bool redirect)
    {
        using var handler = new Handler(_ => new(HttpStatusCode.NotFound));
        CrashSymbolOptions options = redirect ? new CrashSymbolOptions { RedirectOrigins = [new(origin)] } : new CrashSymbolOptions { PublicOrigin = new(origin) };
        CrashSymbolException error = Assert.Throws<CrashSymbolException>(() => new PublicSymbolClient(handler, options));
        Assert.Equal(CrashFailureCode.ConfigurationInvalid, error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Should_allow_only_the_approved_microsoft_origin_subset()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/redirected", StringComparison.Ordinal)
            ? new(HttpStatusCode.NotFound)
            : new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://msdl.microsoft.com/redirected") } });
        using var client = new PublicSymbolClient(handler, new() { RedirectOrigins = [new("https://msdl.microsoft.com/")] });
        await using CrashAttemptWorkspace attempt = await Workspace().CreateAsync(Guid.NewGuid(), default);
        Assert.Equal(PublicSymbolStatus.NotFound, (await client.FetchAsync(s_module, attempt, new(), default)).Status);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_bound_headers_redirects_and_body_together_and_preserve_cancellation(bool cancel)
    {
        var clock = new FakeTimeProvider();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(_ =>
        {
            clock.Advance(TimeSpan.FromSeconds(25));
            return new(HttpStatusCode.OK) { Content = new StreamContent(new S3CrashSymbolStoreShould.WaitingStream(reading)) };
        });
        using var client = new PublicSymbolClient(handler, new(), clock);
        await using CrashAttemptWorkspace attempt = await Workspace().CreateAsync(Guid.NewGuid(), default);
        using var caller = new CancellationTokenSource();
        Task<PublicSymbolResult> fetch = client.FetchAsync(s_module, attempt, new(), caller.Token);
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(fetch.IsCompleted);
        if (cancel)
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        else
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(PublicSymbolStatus.Transient, (await fetch.WaitAsync(TimeSpan.FromSeconds(3))).Status);
        }
        Assert.Empty(Directory.GetFiles(attempt.DirectoryPath));
    }

    [Theory]
    [InlineData("runtime.exe", "runtime.pdb")]
    [InlineData("ntdll.dll", "runtime.pdb")]
    [InlineData("vulkan-1.dll", "vulkan-1.pdb")]
    [InlineData("ntdll.dll", "../ntdll.pdb")]
    public void Should_never_query_private_runtime_symbols_publicly(string image, string pdb) =>
        Assert.False(PublicSymbolClient.IsEligible(s_module with { Name = image, PdbName = pdb }));

    [Fact]
    public async Task Should_reject_redirects_outside_approved_origins()
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.invalid/ntdll.pdb") } });
        await using CrashAttemptWorkspace attempt = await Workspace().CreateAsync(Guid.NewGuid(), default);
        using var client = new PublicSymbolClient(handler, new());
        Assert.Equal(PublicSymbolStatus.Invalid, (await client.FetchAsync(s_module, attempt, new(), default)).Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Should_bound_compressed_symbol_expansion()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent("MSCFmalicious-compressed-body"u8.ToArray()) });
        await using CrashAttemptWorkspace attempt = await Workspace().CreateAsync(Guid.NewGuid(), default);
        using var client = new PublicSymbolClient(handler, new());
        Assert.Equal(PublicSymbolStatus.UnsupportedCompressed, (await client.FetchAsync(s_module, attempt, new(), default)).Status);
        Assert.Empty(Directory.GetFiles(attempt.DirectoryPath));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 86400)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 60)]
    public async Task Should_use_finite_negative_cache_and_short_transient_backoff(HttpStatusCode status, int seconds)
    {
        var clock = new FakeTimeProvider();
        using var handler = new Handler(_ => new(status));
        await using CrashAttemptWorkspace attempt = await Workspace().CreateAsync(Guid.NewGuid(), default);
        using var client = new PublicSymbolClient(handler, new(), clock);
        await client.FetchAsync(s_module, attempt, new(), default);
        int first = handler.Calls;
        Assert.True(first > 0);
        clock.Advance(TimeSpan.FromSeconds(seconds - 1));
        await client.FetchAsync(s_module, attempt, new(), default);
        Assert.Equal(first, handler.Calls);
        clock.Advance(TimeSpan.FromSeconds(1));
        await client.FetchAsync(s_module, attempt, new(), default);
        Assert.True(handler.Calls > first);
    }

    internal static CrashWorkspace Workspace() => new(new CrashToolOptions { ScratchRoot = Path.Combine(Path.GetTempPath(), "symbols-test-" + Guid.NewGuid().ToString("N")) });
    internal sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response(request));
        }
    }
}
