using OpenTelemetry;

namespace Avalon.Api.Commerce;

internal sealed class PaymentSecretProtectionHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using IDisposable suppressed = SuppressInstrumentationScope.Begin();
        HttpResponseMessage response = await base.SendAsync(request, ct);
        // Stripe's SDK prints this response header directly, bypassing application logging policy.
        response.Headers.Remove("Stripe-Notice");
        return response;
    }
}
