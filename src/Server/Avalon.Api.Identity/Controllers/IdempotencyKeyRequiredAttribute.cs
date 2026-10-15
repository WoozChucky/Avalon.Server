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
/// Every action that binds the header carries this attribute (<c>IdempotencyKeyShould</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed partial class IdempotencyKeyRequiredAttribute : Attribute, IResourceFilter
{
    public const string Header = "Idempotency-Key";

    private readonly object _refusal;
    private readonly bool _gameAuthentication;

    /// <param name="reply">The action's reply type, which the refusal is written in.</param>
    public IdempotencyKeyRequiredAttribute(Type reply)
    {
        const string Error = GameAuthErrors.IdempotencyKeyRequired;
        _gameAuthentication = reply == typeof(GameAuthReply);
        _refusal = reply == typeof(GameAuthReply) ? GameAuthReply.Failure(Error)
            : reply == typeof(GameJoinReply) ? new GameJoinReply(Error)
            : reply == typeof(SteamWebLinkReply) ? new SteamWebLinkReply(GameAuthStates.Pending, Error)
            : reply == typeof(LinkBrowserReply) ? new LinkBrowserReply(GameAuthStates.Pending, Error)
            : throw new ArgumentException($"No idempotency key refusal is written as {reply}.", nameof(reply));
    }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        HttpRequest request = context.HttpContext.Request;
        if (!request.IsHttps) return;
        StringValues keys = request.Headers[Header];
        if (keys.Count == 1 && Guid.TryParse(keys[0], out Guid key) && key != Guid.Empty) return;

        // The controllers' [ResponseCache(NoStore = true, Location = None)] is an action filter, which never runs once a
        // resource filter has answered: the refusal carries the same headers itself.
        IHeaderDictionary headers = context.HttpContext.Response.Headers;
        headers.CacheControl = "no-store,no-cache";
        headers.Pragma = "no-cache";

        // Game authentication logs every answer it gives (GameAuthController.LogResult); a refusal here is one too.
        if (_gameAuthentication &&
            context.HttpContext.RequestServices.GetService<ILogger<GameAuthController>>() is { } logger)
        {
            Log.Responded(logger, context.ActionDescriptor.RouteValues["action"] ?? string.Empty,
                StatusCodes.Status400BadRequest, GameAuthErrors.IdempotencyKeyRequired);
        }

        context.Result = new BadRequestObjectResult(_refusal);
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Game authentication {Operation} responded {StatusCode} with {ErrorCode}")]
        public static partial void Responded(ILogger logger, string operation, int statusCode, string errorCode);
    }
}
