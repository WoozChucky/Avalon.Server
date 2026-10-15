using Avalon.Api.Controllers;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;

namespace Avalon.Api.Identity.Controllers;

/// <summary>
/// The action requires an <c>Idempotency-Key</c> header: one GUID, not all zeros. A request whose key is missing, empty,
/// not a GUID, all zeros or sent more than once is answered 400 <see cref="GameAuthErrors.IdempotencyKeyRequired"/>, in
/// the reply type the action answers with, before the request is bound: the action never runs, so no attempt, budget or
/// record is touched. Bound as a <see cref="Guid"/> alone, a missing key reached the services as
/// <see cref="Guid.Empty"/> and was refused as a bad credential (401 <c>INVALID_ATTEMPT</c>), and an unparsable one as
/// a validation problem. A plaintext request is left to the action, which refuses it as plaintext first, as before.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class IdempotencyKeyRequiredAttribute : Attribute, IResourceFilter
{
    public const string Header = "Idempotency-Key";

    private readonly object _refusal;

    /// <param name="reply">The action's reply type, which the refusal is written in.</param>
    public IdempotencyKeyRequiredAttribute(Type reply)
    {
        const string Error = GameAuthErrors.IdempotencyKeyRequired;
        _refusal = reply == typeof(GameAuthReply) ? GameAuthReply.Failure(Error)
            : reply == typeof(GameJoinReply) ? new GameJoinReply(Error)
            : reply == typeof(SteamWebLinkReply) ? new SteamWebLinkReply(GameAuthStates.Pending, Error)
            : reply == typeof(LinkBrowserReply) ? new LinkBrowserReply(GameAuthStates.Pending, Error)
            : throw new ArgumentException($"No idempotency key refusal is written as {reply}.", nameof(reply));
    }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (!context.HttpContext.Request.IsHttps) return;
        StringValues keys = context.HttpContext.Request.Headers[Header];
        if (keys.Count == 1 && Guid.TryParse(keys[0], out Guid key) && key != Guid.Empty) return;
        context.Result = new BadRequestObjectResult(_refusal);
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
