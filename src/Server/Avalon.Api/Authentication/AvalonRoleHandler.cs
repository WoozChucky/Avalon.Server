using System.Security.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Authentication;

public class AvalonRoleHandler : AuthorizationHandler<AvalonRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AvalonRoleRequirement requirement)
    {
        if (!context.User.Identity!.IsAuthenticated)
        {
            context.Fail();
            throw new AuthenticationException("User is not authenticated");
        }

        var role = context.User.FindFirstValue(ClaimTypes.Role);

        if (role == null)
        {
            context.Fail();
            throw new UnauthorizedAccessException("User doest not have required permission.");
        }

        if (role == requirement.Role)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
            throw new UnauthorizedAccessException("User doest not have required permission.");
        }

        return Task.CompletedTask;
    }
}
