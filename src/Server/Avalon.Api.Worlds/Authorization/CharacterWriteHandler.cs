using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Authorization;
using Avalon.Domain.Characters;
using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Worlds.Authorization;

public sealed class CharacterWriteHandler : AuthorizationHandler<WriteRequirement, Character>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, WriteRequirement requirement, Character resource)
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
