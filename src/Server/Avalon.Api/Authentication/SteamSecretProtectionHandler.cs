using OpenTelemetry;

namespace Avalon.Api.Authentication;

/// <summary>Valve requires query credentials. Suppress their HTTP spans; only sanitized outcome metrics may be emitted.</summary>
internal sealed class SteamSecretProtectionHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using IDisposable suppressed = SuppressInstrumentationScope.Begin();
        return await base.SendAsync(request, cancellationToken);
    }
}
