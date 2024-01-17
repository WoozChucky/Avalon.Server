namespace Avalon.Api.Contract;

public class RegisterResponse
{
    public string Token { get; set; }
    public long ExpiresAt { get; set; }
}
