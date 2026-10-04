using Avalon.Api;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenTelemetry;
using Xunit;

namespace Avalon.Api.UnitTests.StoreAuth;

public class SteamHttpRegistrationShould
{
    [Theory]
    [InlineData(StoreAuthenticationTestData.SteamAppId)]
    [InlineData(123456)]
    public async Task Suppress_query_spans_and_logs_on_the_registered_provider_pipeline(uint appId)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var logs = new CapturedLogs();
        var transport = new SuppressionProbe();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Application:StoreAuthentication:SteamPublisherKey"] = SteamProofVerifierShould.Key,
            ["Application:StoreAuthentication:SteamAppId"] = appId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build());
        services.AddSingleton(environment);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSteamStoreAuthentication();
        services.AddHttpClient<ISteamProofVerifier, SteamProofVerifier>().ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<Avalon.Configuration.StoreAuthenticationConfiguration>>().Value.Validate(production: true);
        var result = await provider.GetRequiredService<ISteamProofVerifier>().VerifyAsync("ABCD",
            SteamProofVerifierShould.Identity, CancellationToken.None);
        Assert.Equal(SteamProofStatus.ProviderUnavailable, result.Status);
        Assert.True(transport.Suppressed);
        Assert.Equal(2, transport.Calls);
        Assert.All(transport.Requests, request => Assert.Contains("appid=" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Query, StringComparison.Ordinal));
        Assert.DoesNotContain(SteamProofVerifierShould.Key, string.Join("\n", logs.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain("ABCD", string.Join("\n", logs.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain("key=", string.Join("\n", logs.Messages), StringComparison.Ordinal);
    }

    private sealed class SuppressionProbe : HttpMessageHandler
    {
        public bool Suppressed { get; private set; } = true;
        public int Calls { get; private set; }
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(request.RequestUri!);
            Suppressed &= Sdk.SuppressInstrumentation;
            throw new HttpRequestException(request.RequestUri!.ToString());
        }
    }

    private sealed class CapturedLogs : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
        public void Dispose() { }
    }
}
