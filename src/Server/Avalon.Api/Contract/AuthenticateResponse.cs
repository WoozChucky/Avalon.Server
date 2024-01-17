namespace Avalon.Api.Contract;

public class AuthenticateResponse
{
    public string Token { get; set; }
    public long ExpiresAt { get; set; }
}
