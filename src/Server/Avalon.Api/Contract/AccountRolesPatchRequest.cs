using Avalon.Domain.Auth;

namespace Avalon.Api.Contract;

public sealed class AccountRolesPatchRequest
{
    public AccountAccessLevel Roles { get; set; }
}
