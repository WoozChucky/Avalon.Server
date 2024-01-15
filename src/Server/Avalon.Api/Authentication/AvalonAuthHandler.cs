using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Claims;
using Avalon.Api.Services;
using Avalon.Database.Auth;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;

namespace Avalon.Api.Authentication;

public class AvalonAuthHandler : IAuthorizationHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAccountService _accountService;
    
    public AvalonAuthHandler(IHttpContextAccessor httpContextAccessor, IAccountService accountService)
    {
        _httpContextAccessor = httpContextAccessor;
        _accountService = accountService;
    }
    
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (!context.PendingRequirements.Any())
            return;
        
        var pendingRequirements = context.PendingRequirements.ToList();
            
        if (pendingRequirements.FirstOrDefault(r => r is AvalonAuthRequirement) is AvalonAuthRequirement authRequirement)
        {
            await HandleRequirementAsync(context, authRequirement);
        }
    }
    
    private async Task HandleRequirementAsync(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        if (context.User.Identity is { IsAuthenticated: false })
        {
            context.Fail();
            throw new AuthenticationException("User is not authenticated");
        }
        
        if (!_httpContextAccessor.HttpContext!.Request.Headers.TryGetValue(HeaderNames.Authorization, out var token))
        {
            context.Fail();
            throw new AuthenticationException("User is not authenticated");
        }

        var accountId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (accountId == null)
        {
            context.Fail();
            throw new AuthenticationException("User is not authenticated");
        }

        var authContext = _httpContextAccessor.HttpContext.RequestServices.GetRequiredService<IAuthContext>();

        var account = await _accountService.FindById(int.Parse(accountId));
        
        if (account == null)
        {
            context.Fail();
            throw new AuthenticationException("User is not authenticated");
        }
        
        authContext.Load(account);
        
        _httpContextAccessor.HttpContext.Items[nameof(IAuthContext)] = authContext;
        _httpContextAccessor.HttpContext.Items[nameof(Account)] = account;
        
        context.Succeed(requirement);
    }
}
