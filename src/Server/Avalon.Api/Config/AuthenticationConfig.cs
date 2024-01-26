namespace Avalon.Api.Config;

public class AuthenticationConfig
{
    public string IssuerSigningKey { get; set; } = string.Empty;
    public bool ValidateIssuerKey { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public bool ValidateIssuer { get; set; }
    public string Audience { get; set; } = string.Empty;
    public bool ValidateAudience { get; set; }
    public int ClockSkewInMinutes { get; set; }
}
