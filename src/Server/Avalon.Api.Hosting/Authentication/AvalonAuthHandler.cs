using System.Security.Authentication;
using System.Security.Claims;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Hosting.Authentication;

/// <summary>
/// The default policy's requirement (<see cref="AvalonAuthRequirement"/>): the caller's account, the one authentication
/// already loaded and checked or, for a principal some other scheme produced, one read here through
/// <see cref="IAccountRepository"/>, is Active, and is loaded into <see cref="IAuthContext"/> for the request.
/// </summary>
public class AvalonAuthHandler : IAuthorizationHandler
{
    private readonly IAccountRepository _accounts;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AvalonAuthHandler> _logger;

    public AvalonAuthHandler(ILoggerFactory loggerFactory, IHttpContextAccessor httpContextAccessor,
        IAccountRepository accounts)
    {
        _logger = loggerFactory.CreateLogger<AvalonAuthHandler>();
        _httpContextAccessor = httpContextAccessor;
        _accounts = accounts;
    }

    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (!context.PendingRequirements.Any())
        {
            return;
        }

        if (context.User.Identity is { IsAuthenticated: false })
        {
            context.Fail();
            throw new AuthenticationException("User is not authenticated");
        }

        var pendingRequirements = context.PendingRequirements.ToList();

        if (pendingRequirements.FirstOrDefault(r =>
                r is AvalonAuthRequirement) is AvalonAuthRequirement authRequirement)
        {
            await HandleRequirementAsync(context, authRequirement);
        }
    }

    private async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        IAuthorizationRequirement requirement)
    {
        // No Authorization-header check: authentication has already run, and a JWT may arrive in
        // the session cookie instead (#480). What matters is the authenticated account below.
        HttpContext http = _httpContextAccessor.HttpContext!;

        string? accountId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (accountId == null)
        {
            context.Fail();
            throw new AuthenticationException("User is not authenticated");
        }

        IAuthContext authContext = http.RequestServices.GetRequiredService<IAuthContext>();

        // Authentication already loaded and checked this account (AccountAccessCheck); reuse it.
        // The fallback load is for a principal some other scheme produced.
        Account? account = AccountAccessCheck.Recall(http, accountId);
        if (account is null)
        {
            _logger.LogInformation("Loading account {AccountId}", accountId);
            account = await _accounts.FindByIdAsync((AccountId)accountId, track: false, http.RequestAborted);
        }

        // A recalled account already passed this check; a freshly loaded one must pass it too.
        if (!AccountAccessCheck.MayHoldSession(account))
        {
            context.Fail();
            throw new AuthenticationException("User is not authenticated");
        }

        authContext.Load(account);

        http.Items[nameof(IAuthContext)] = authContext;
        http.Items[nameof(Account)] = account;

        context.Succeed(requirement);
    }
}
