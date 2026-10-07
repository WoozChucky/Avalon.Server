namespace Avalon.Api.Contract;

public class RefreshResponse
{
    public string Token { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
}
