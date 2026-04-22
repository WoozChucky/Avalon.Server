using Avalon.Domain.Auth;
namespace Avalon.Api.Contract;
public sealed class CreatePatRequest
{
    public string Name { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public AccountAccessLevel? Roles { get; set; }
}
