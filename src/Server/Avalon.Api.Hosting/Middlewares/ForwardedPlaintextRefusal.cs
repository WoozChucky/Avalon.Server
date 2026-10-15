using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Avalon.Api.Hosting.Middlewares;

/// <summary>
/// Refuses a request that reached the edge proxy over plaintext, before any endpoint runs: the API's own line behind the
/// proxy's redirect to https, so a proxy that stops redirecting cannot put a password or a token on the wire unnoticed.
/// <para>
/// After the forwarded-headers middleware, <c>Request.Scheme</c> is http both for a request the proxy forwarded as http
/// and for a call made straight to the pod over cluster http (a world server, a probe, the balance proxy), which must
/// keep working. The two are told apart by who sent the request: <see cref="Observe"/> runs before that middleware and
/// notes a request whose own peer is a proxy <c>Application:ForwardedHeaders</c> trusts (loopback included) and that
/// carries <c>X-Forwarded-Proto</c>; <see cref="RefuseOrContinue"/> runs after it and refuses such a request when the
/// scheme it ends with, the one the forwarded-headers middleware believed, is not https. A request with no
/// <c>X-Forwarded-Proto</c> is served as before, and so is one from a peer that is not trusted, whose header the
/// forwarded-headers middleware ignores too: a caller can use the header only to get itself refused.
/// </para>
/// <para>
/// Every method gets the same answer, 403 ProblemDetails <see cref="Detail"/>, and never a redirect: the request has
/// already crossed the network in plaintext, which a redirect cannot undo, and a client that silently followed one
/// would hide the proxy fault this exists to expose; a redirect would also be built from a <c>Host</c> the forwarded
/// headers do not vouch for. Redirecting browsers to https is the edge proxy's job.
/// </para>
/// </summary>
public static class ForwardedPlaintextRefusal
{
    /// <summary>The ProblemDetails <c>Detail</c> of the refusal.</summary>
    public const string Detail = "HTTPS_REQUIRED";

    // Set in HttpContext.Items for a request a trusted proxy forwarded with a scheme; only its presence is read.
    private static readonly object s_forwardedKey = new();

    /// <summary>
    /// Before the forwarded-headers middleware: notes a request whose peer is a trusted proxy and that carries
    /// <c>X-Forwarded-Proto</c>.
    /// </summary>
    internal static void Observe(HttpContext context, ForwardedHeadersOptions options)
    {
        if (!context.Request.Headers.ContainsKey(ForwardedHeadersDefaults.XForwardedProtoHeaderName))
            return;

        IPAddress? peer = context.Connection.RemoteIpAddress;
        if (peer is not null && ForwardedHeadersSetup.IsTrustedProxy(options, peer))
            context.Items[s_forwardedKey] = s_forwardedKey;
    }

    /// <summary>
    /// After the forwarded-headers middleware: 403 for a request <see cref="Observe"/> noted whose scheme is not https,
    /// the rest of the pipeline for every other.
    /// </summary>
    internal static Task RefuseOrContinue(HttpContext context, RequestDelegate next)
    {
        if (context.Request.IsHttps || !context.Items.ContainsKey(s_forwardedKey))
            return next(context);

        return ExceptionHandlerMiddleware.WriteProblemAsync(context, StatusCodes.Status403Forbidden, "HttpsRequired",
            "HTTPS required", Detail);
    }
}
