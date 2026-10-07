using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Authorization;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Identity.Authorization;

public sealed class PatWriteHandler : AuthorizationHandler<WriteRequirement, PersonalAccessToken>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, WriteRequirement requirement, PersonalAccessToken resource)
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
