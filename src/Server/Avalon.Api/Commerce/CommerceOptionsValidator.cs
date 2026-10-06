using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Commerce;

public sealed class CommerceOptionsValidator(IHostEnvironment? host = null, IOptions<StoreAuthenticationConfiguration>? authentication = null,
    IEnumerable<PaymentProviderRegistration>? providers = null)
    : IValidateOptions<CommerceConfiguration>
{
    public ValidateOptionsResult Validate(string? name, CommerceConfiguration config)
    {
        if (!config.Enabled) return ValidateOptionsResult.Success;
        var isolated = host?.IsDevelopment() == true && authentication?.Value.Environment == CommerceEnvironments.DevelopmentLicense &&
            authentication.Value.SteamIdentityPrefix == CommerceEnvironments.DevelopmentIdentityPrefix;
        var registration = providers?.SingleOrDefault(x => string.Equals(x.Name, config.Provider, StringComparison.Ordinal));
        var valid = isolated && registration is not null && config.PaymentEnvironment == CommerceEnvironments.Sandbox && config.LicenseEnvironment == authentication?.Value.Environment &&
            config.Product == StoreAuthenticationConfiguration.Product && config.ProviderProduct == StoreAuthenticationConfiguration.NativeProviderProduct &&
            config.AmountMinor > 0 && ValidCurrency(config.Currency) && config.Quantity == CommercePolicy.GameLicenseQuantity &&
            Text(config.OfferId, 128) && Text(config.ProviderPriceId, 256) && Text(config.ProviderCatalogProductId, 256) && Text(config.ProviderAccountId, 128) &&
            Text(config.ApiKey, 256) && Text(config.WebhookSecret, 256) && registration.SettingsAreValid(config) && ValidOrigin(config.PublicSiteOrigin) &&
            config.PaymentMethods is { Length: > 0 } && config.PaymentMethods.Distinct(StringComparer.Ordinal).Count() == config.PaymentMethods.Length &&
            config.PaymentMethods.All(x => Text(x, 32) && x.All(c => char.IsAsciiLetterLower(c) || c == '_'));
        return valid ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("Enabled commerce requires a valid configured offer, registered payment provider and isolated Development sandbox with server credentials.");
    }
    internal static bool Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && value == value.Trim();
    internal static bool Identifier(string value, string prefix) => Text(value, 256) && value.StartsWith(prefix, StringComparison.Ordinal) && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    internal static bool ValidCurrency(string value) => value is { Length: 3 } && value.All(char.IsAsciiLetterLower);
    internal static bool ValidOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var origin) && origin.Scheme == "https" &&
        origin.UserInfo.Length == 0 && origin.Query.Length == 0 && origin.Fragment.Length == 0 && origin.AbsolutePath == "/" && origin.IsDefaultPort;
}
