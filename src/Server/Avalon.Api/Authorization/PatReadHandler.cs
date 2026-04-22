using Avalon.Api.Authentication;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Authorization;

public sealed class PatReadHandler : AuthorizationHandler<ReadRequirement, PersonalAccessToken>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ReadRequirement requirement, PersonalAccessToken resource)
    {
        if (context.User.HasRoleAtLeast(AvalonRoles.Admin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (resource.AccountId == context.User.AccountId())
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
