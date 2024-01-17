using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Authentication;

public class AvalonRoleRequirement: IAuthorizationRequirement
{
    public string Role { get; }
    
    public AvalonRoleRequirement(string role)
    {
        Role = role;
    }
}
