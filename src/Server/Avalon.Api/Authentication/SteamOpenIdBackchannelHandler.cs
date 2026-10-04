using Avalon.Common.GameAuth;
using System.Net;
using OpenTelemetry;

namespace Avalon.Api.Authentication;

/// <summary>No discovery, profile lookup, redirects, payload logs or provider tracing.</summary>
public sealed class SteamOpenIdBackchannelHandler : DelegatingHandler
{
    public SteamOpenIdBackchannelHandler() : base(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, ConnectTimeout = GameAuthPolicy.TransportTimeout, MaxConnectionsPerServer = 32,
        ActivityHeadersPropagator = null,
    }) { }
    public SteamOpenIdBackchannelHandler(HttpMessageHandler transport) : base(transport) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri?.AbsoluteUri != SteamWebLinkOptions.ProviderEndpoint)
            throw new HttpRequestException("Invalid Steam verification destination.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GameAuthPolicy.TransportTimeout);
        using var suppression = SuppressInstrumentationScope.Begin();
        using var response = await base.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode) return new(HttpStatusCode.BadGateway) { Content = new StringContent("is_valid:false") };
        if (response.Content.Headers.ContentLength > GameAuthPolicy.MaximumBodyBytes) throw new HttpRequestException("Steam verification response too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024]; int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) != 0)
        {
            if (buffer.Length + read > GameAuthPolicy.MaximumBodyBytes) throw new HttpRequestException("Steam verification response too large.");
            buffer.Write(chunk, 0, read);
        }
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
    }
}
