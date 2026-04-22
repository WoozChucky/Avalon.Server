using Avalon.Api.Authentication;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Authorization;

public sealed class AccountWriteHandler : AuthorizationHandler<WriteRequirement, Account>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, WriteRequirement requirement, Account resource)
    {
        if (context.User.HasRoleAtLeast(AvalonRoles.Admin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (resource.Id == context.User.AccountId())
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
