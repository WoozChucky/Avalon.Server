using Avalon.Api.Commerce;
using Avalon.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Commerce;

public sealed class CommerceConfigurationShould
{
    [Fact]
    public void Disabled_configuration_needs_no_credentials() => Assert.True(Validator(false).Validate(null, new()).Succeeded);

    [Fact]
    public void Allow_only_the_isolated_development_sandbox() => Assert.True(Validator(true).Validate(null, Valid()).Succeeded);

    [Theory]
    [InlineData("live")]
    [InlineData("production")]
    [InlineData("price")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("quantity")]
    [InlineData("methods")]
    [InlineData("origin")]
    [InlineData("key")]
    public void Reject_unsafe_enabled_configuration_without_echoing_secrets(string defect)
    {
        var config = Valid();
        switch (defect)
        {
            case "live": config.PaymentEnvironment = "live"; break;
            case "production": config.LicenseEnvironment = "production"; break;
            case "price": config.ProviderPriceId = ""; break;
            case "amount": config.AmountMinor = 0; break;
            case "currency": config.Currency = "US dollars"; break;
            case "quantity": config.Quantity = 2; break;
            case "methods": config.PaymentMethods = []; break;
            case "origin": config.PublicSiteOrigin = "https://example.test/?secret=test"; break;
            case "key": config.ApiKey = "sk_live_private"; break;
        }
        var result = Validator(true).Validate(null, config);
        Assert.True(result.Failed);
        Assert.DoesNotContain(config.ApiKey, result.FailureMessage!);
    }

    [Fact]
    public void Production_host_cannot_enable_sandbox() => Assert.True(Validator(false).Validate(null, Valid()).Failed);

    [Fact]
    public void Missing_method_list_is_a_safe_configuration_failure()
    {
        var config = Valid();
        config.PaymentMethods = null!;
        Assert.True(Validator(true).Validate(null, config).Failed);
    }

    [Fact]
    public void Amount_and_currency_are_configuration_and_another_provider_can_supply_its_own_settings_rules()
    {
        var config = Valid();
        config.AmountMinor = 1200;
        config.Currency = "usd";
        Assert.True(Validator(true).Validate(null, config).Succeeded);
        config.Provider = "another-provider";
        config.ProviderPriceId = "offer-123";
        config.ProviderCatalogProductId = "game-123";
        config.ProviderAccountId = "merchant-123";
        config.ApiKey = "opaque-provider-key";
        config.WebhookSecret = "opaque-signing-key";
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName = Environments.Development;
        var validator = new CommerceOptionsValidator(host, Options.Create(new StoreAuthenticationConfiguration
            { Environment = "development", SteamIdentityPrefix = "avalon-auth-dev" }),
            [new PaymentProviderRegistration("another-provider", _ => true)]);
        Assert.True(validator.Validate(null, config).Succeeded);
        config.Provider = "unregistered-provider";
        Assert.True(validator.Validate(null, config).Failed);
    }

    internal static CommerceConfiguration Valid() => new()
    {
        Enabled = true, PublicSiteOrigin = "https://example.test", OfferId = "base-eur",
        ProviderPriceId = "price_test", ProviderCatalogProductId = "prod_test", ProviderAccountId = "acct_test",
        ApiKey = "sk_test_private", WebhookSecret = "whsec_private",
    };

    private static CommerceOptionsValidator Validator(bool development)
    {
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName = development ? Environments.Development : Environments.Production;
        return new(host, Options.Create(new StoreAuthenticationConfiguration { Environment = "development", SteamIdentityPrefix = "avalon-auth-dev" }), [StripePaymentProvider.Registration]);
    }
}
