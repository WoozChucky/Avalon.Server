namespace Avalon.Api.Contract;

public class RegisterResponse
{
    public string Token { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
}
