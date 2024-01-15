namespace Avalon.Api.Config;

public class AuthenticationConfig
{
    public string IssuerSigningKey { get; set; }
    public bool ValidateIssuerKey { get; set; }
    public string Issuer { get; set; }
    public bool ValidateIssuer { get; set; }
    public string Audience { get; set; }
    public bool ValidateAudience { get; set; }
    public int ClockSkewInMinutes { get; set; }
}
