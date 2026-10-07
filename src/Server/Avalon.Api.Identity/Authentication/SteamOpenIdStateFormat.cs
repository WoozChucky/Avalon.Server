using System.Security.Cryptography;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;

namespace Avalon.Api.Identity.Authentication;

public sealed class SteamOpenIdStateFormat(GameAuthCryptography crypto) : ISecureDataFormat<AuthenticationProperties>
{
    private const string Binding = "avalon.steam-web.openid-state.v1";
    public string Protect(AuthenticationProperties data) => Protect(data, null);
    public string Protect(AuthenticationProperties data, string? purpose) => WebEncoders.Base64UrlEncode(
        System.Text.Encoding.UTF8.GetBytes(crypto.ProtectText(Convert.ToBase64String(PropertiesSerializer.Default.Serialize(data)), Binding + purpose)));
    public AuthenticationProperties? Unprotect(string? protectedText) => Unprotect(protectedText, null);
    public AuthenticationProperties? Unprotect(string? protectedText, string? purpose)
    {
        if (protectedText is null || protectedText.Length > 8192) return null;
        try
        {
            return PropertiesSerializer.Default.Deserialize(Convert.FromBase64String(crypto.UnprotectText(
            System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(protectedText)), Binding + purpose)));
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException) { return null; }
    }
}
