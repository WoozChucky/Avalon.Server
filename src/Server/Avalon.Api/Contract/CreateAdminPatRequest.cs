using Avalon.Domain.Auth;
namespace Avalon.Api.Contract;
public sealed class CreateAdminPatRequest
{
    public long AccountId { get; set; }
    public string Name { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public AccountAccessLevel Roles { get; set; }
}
