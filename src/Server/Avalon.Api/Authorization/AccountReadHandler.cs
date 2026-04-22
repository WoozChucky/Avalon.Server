using Avalon.Api.Authentication;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Authorization;

public sealed class AccountReadHandler : AuthorizationHandler<ReadRequirement, Account>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ReadRequirement requirement, Account resource)
    {
        if (context.User.HasRoleAtLeast(AvalonRoles.GameMaster))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (resource.Id == context.User.AccountId())
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
