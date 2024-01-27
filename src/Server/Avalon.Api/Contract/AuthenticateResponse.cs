using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

public class AuthenticateResponse
{
    public string? Token { get; set; }
    public long? ExpiresAt { get; set; }
    public string? MfaHash { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AuthenticationResponseStatus Status { get; set; }
}

public enum AuthenticationResponseStatus
{
    Failure,
    Success,
    RequiresMFA
}
