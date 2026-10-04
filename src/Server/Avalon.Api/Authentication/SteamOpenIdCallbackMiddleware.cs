using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using System.Threading.RateLimiting;

namespace Avalon.Api.Authentication;

public sealed class SteamOpenIdCallbackMiddleware(RequestDelegate next)
{
    private const int PermitsPerMinute = 16;
    private const int MaximumValues = 32;
    private const int MaximumValueCharacters = 8192;
    private const int MaximumKeyCharacters = 128;
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);
    private readonly PartitionedRateLimiter<HttpContext> _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { PermitLimit = PermitsPerMinute, Window = RateWindow, QueueLimit = 0 }));
    public static bool IsCallback(PathString path) => path.Value?.EndsWith(SteamWebLinkOptions.CallbackPath, StringComparison.OrdinalIgnoreCase) == true;
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsCallback(context.Request.Path)) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        using var permit = await _limiter.AcquireAsync(context, cancellationToken: context.RequestAborted);
        if (!permit.IsAcquired) { context.Response.StatusCode = 429; return; }
        if (!context.Request.IsHttps || context.Request.QueryString.Value?.Length > GameAuthPolicy.MaximumBodyBytes || context.Request.ContentLength > GameAuthPolicy.MaximumBodyBytes ||
            context.Request.Query["state"].Count != 1) { context.Response.StatusCode = 400; return; }
        try
        {
            IEnumerable<KeyValuePair<string, StringValues>> parameters;
            if (HttpMethods.IsGet(context.Request.Method)) parameters = context.Request.Query;
            else if (HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType)
            {
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = GameAuthPolicy.MaximumBodyBytes;
                context.Features.Set<IFormFeature>(new FormFeature(context.Request, new FormOptions
                { BufferBodyLengthLimit = GameAuthPolicy.MaximumBodyBytes, ValueCountLimit = MaximumValues, ValueLengthLimit = MaximumValueCharacters, KeyLengthLimit = MaximumKeyCharacters }));
                parameters = await context.Request.ReadFormAsync(context.RequestAborted);
            }
            else { context.Response.StatusCode = 400; return; }
            var values = parameters.ToArray();
            if (values.Length > MaximumValues || values.Any(p => p.Value.Count != 1 || p.Key.Length > MaximumKeyCharacters || p.Value.ToString().Length > MaximumValueCharacters))
            { context.Response.StatusCode = 400; return; }
            var map = values.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);
            var required = new[] { "op_endpoint", "claimed_id", "identity", "return_to", "response_nonce", "assoc_handle" };
            var signed = map.GetValueOrDefault("openid.signed", string.Empty).Split(',');
            if (map.GetValueOrDefault("openid.op_endpoint") != SteamWebLinkOptions.ProviderEndpoint ||
                map.GetValueOrDefault("openid.claimed_id") != map.GetValueOrDefault("openid.identity") ||
                SteamWebLinkStore.SteamSubject(map.GetValueOrDefault("openid.claimed_id", string.Empty)) is null ||
                required.Any(k => !signed.Contains(k, StringComparer.Ordinal)) ||
                !SteamWebLinkStore.FreshNonce(map.GetValueOrDefault("openid.response_nonce", string.Empty),
                    context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime))
            { context.Response.StatusCode = 400; return; }
            context.Items["steam-link.nonce"] = map["openid.response_nonce"];
            await next(context);
        }
        catch (InvalidDataException) { context.Response.StatusCode = 400; }
        catch (BadHttpRequestException) { context.Response.StatusCode = 400; }
    }
}
