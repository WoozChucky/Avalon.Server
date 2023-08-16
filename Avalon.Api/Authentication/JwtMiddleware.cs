using Avalon.Api.Services;
using Avalon.Database.Auth;
using Microsoft.Net.Http.Headers;

namespace Avalon.Api.Authentication;

public class JwtMiddleware
{
    private readonly RequestDelegate _next;

    public JwtMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, IAccountService accountService, IJwtUtils jwtUtils)
    {
        var token = context.Request.Headers[HeaderNames.Authorization].FirstOrDefault()?.Split(" ").Last();
        var accountId = jwtUtils.ValidateJwtToken(token);
        if (accountId != null)
        {
            // attach user to context on successful jwt validation
            context.Items[nameof(Account)] = await accountService.FindById(accountId.Value);
        }

        await _next(context);
    }
}
