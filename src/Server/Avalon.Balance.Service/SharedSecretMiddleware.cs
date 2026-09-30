using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Avalon.Balance.Service;

/// <summary>
/// Every endpoint except /health and /alive needs <c>X-Balance-Secret</c> equal to the configured secret. A missing or
/// wrong one is a bare 401: no body, and the secret is never logged.
/// </summary>
public sealed class SharedSecretMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[] _expected;

    public SharedSecretMiddleware(RequestDelegate next, IOptions<BalanceServiceOptions> options)
    {
        _next = next;
        _expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.SharedSecret));
    }

    public Task InvokeAsync(HttpContext context)
    {
        PathString path = context.Request.Path;
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) || path.Equals("/alive", StringComparison.OrdinalIgnoreCase))
            return _next(context);

        if (Matches(context.Request.Headers[BalanceServiceOptions.HeaderName].ToString()))
            return _next(context);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    /// <summary>Compares the SHA-256 of both, so the lengths match and the comparison takes constant time.</summary>
    private bool Matches(string supplied) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), _expected);
}
