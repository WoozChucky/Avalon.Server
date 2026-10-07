using System.Text.Json;
using Avalon.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Commerce.UnitTests;

public sealed class CommerceConfigurationShould
{
    [Fact]
    public void Disabled_configuration_needs_no_credentials() => Assert.True(Validator(false).Validate(null, new()).Succeeded);

    [Fact]
    public void Allow_only_the_isolated_development_sandbox() => Assert.True(Validator(true).Validate(null, Valid()).Succeeded);

    [Theory]
    [InlineData("card")]
    [InlineData("multibanco")]
    public void Bind_exactly_the_configured_payment_methods_without_appending_defaults(string method)
    {
        CommerceConfiguration configured = Valid();
        configured.PaymentMethods = [method];
        using var json = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(configured));
        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
        CommerceConfiguration bound = configuration.Get<CommerceConfiguration>()!;

        Assert.Equal(new[] { method }, bound.PaymentMethods);
        Assert.True(Validator(true).Validate(null, bound).Succeeded);
    }

    [Theory]
    [InlineData("live")]
    [InlineData("production")]
    [InlineData("price")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("quantity")]
    [InlineData("methods")]
    [InlineData("duplicate-methods")]
    [InlineData("origin")]
    [InlineData("key")]
    public void Reject_unsafe_enabled_configuration_without_echoing_secrets(string defect)
    {
        CommerceConfiguration config = Valid();
        switch (defect)
        {
            case "live": config.PaymentEnvironment = "live"; break;
            case "production": config.LicenseEnvironment = "production"; break;
            case "price": config.ProviderPriceId = ""; break;
            case "amount": config.AmountMinor = 0; break;
            case "currency": config.Currency = "US dollars"; break;
            case "quantity": config.Quantity = 2; break;
            case "methods": config.PaymentMethods = []; break;
            case "duplicate-methods": config.PaymentMethods = ["card", "card"]; break;
            case "origin": config.PublicSiteOrigin = "https://example.test/?secret=test"; break;
            case "key": config.ApiKey = "sk_live_private"; break;
        }
        ValidateOptionsResult result = Validator(true).Validate(null, config);
        Assert.True(result.Failed);
        Assert.DoesNotContain(config.ApiKey, result.FailureMessage!);
    }

    [Fact]
    public void Production_host_cannot_enable_sandbox() => Assert.True(Validator(false).Validate(null, Valid()).Failed);

    [Fact]
    public void Missing_method_list_is_a_safe_configuration_failure()
    {
        CommerceConfiguration config = Valid();
        config.PaymentMethods = null!;
        Assert.True(Validator(true).Validate(null, config).Failed);
    }

    [Fact]
    public void Amount_and_currency_are_configuration_and_another_provider_can_supply_its_own_settings_rules()
    {
        CommerceConfiguration config = Valid();
        config.AmountMinor = 1200;
        config.Currency = "usd";
        Assert.True(Validator(true).Validate(null, config).Succeeded);
        config.Provider = "another-provider";
        config.ProviderPriceId = "offer-123";
        config.ProviderCatalogProductId = "game-123";
        config.ProviderAccountId = "merchant-123";
        config.ApiKey = "opaque-provider-key";
        config.WebhookSecret = "opaque-signing-key";
        IHostEnvironment host = Substitute.For<IHostEnvironment>();
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
        Enabled = true,
        PublicSiteOrigin = "https://example.test",
        OfferId = "base-eur",
        ProviderPriceId = "price_test",
        ProviderCatalogProductId = "prod_test",
        ProviderAccountId = "acct_test",
        ApiKey = "sk_test_private",
        WebhookSecret = "whsec_private",
        PaymentMethods = ["card"],
    };

    private static CommerceOptionsValidator Validator(bool development)
    {
        IHostEnvironment host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName = development ? Environments.Development : Environments.Production;
        return new(host, Options.Create(new StoreAuthenticationConfiguration { Environment = "development", SteamIdentityPrefix = "avalon-auth-dev" }), [StripePaymentProvider.Registration]);
    }
}
