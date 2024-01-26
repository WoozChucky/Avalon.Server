namespace Avalon.Api.Contract;

public class AuthenticateResponse
{
    public string Token { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
}
