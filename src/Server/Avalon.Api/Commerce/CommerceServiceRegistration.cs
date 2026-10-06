using Microsoft.Extensions.Options;
using Stripe;

namespace Avalon.Api;

public static class CommerceServiceRegistration
{
    public static void AddCommerce(this IServiceCollection services)
    {
        services.AddOptions<Commerce.CommerceConfiguration>().BindConfiguration("Application:Commerce").ValidateOnStart();
        services.AddSingleton<IValidateOptions<Commerce.CommerceConfiguration>, Commerce.CommerceOptionsValidator>();
        services.AddSingleton(Commerce.StripePaymentProvider.Registration);
#pragma warning disable EXTEXP0001 // Durable operation keys own replay; transport retries and body-bearing diagnostics are suppressed.
        services.AddHttpClient("avalon-commerce", http =>
            {
                http.Timeout = TimeSpan.FromSeconds(20);
                http.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            })
            .RemoveAllLoggers().RemoveAllResilienceHandlers()
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) => { handlers.Clear(); handlers.Add(new Commerce.PaymentSecretProtectionHandler()); })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false, MaxConnectionsPerServer = 16,
                ConnectTimeout = TimeSpan.FromSeconds(5), ActivityHeadersPropagator = null,
            });
#pragma warning restore EXTEXP0001
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IOptions<Commerce.CommerceConfiguration>>().Value;
            return new StripeClient(config.Enabled ? config.ApiKey : "sk_test_disabled",
                httpClient: new SystemNetHttpClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("avalon-commerce"), maxNetworkRetries: 0, enableTelemetry: false),
                apiBase: "https://api.stripe.com");
        });
        services.AddSingleton<Commerce.IPaymentProvider, Commerce.StripePaymentProvider>();
        services.AddSingleton<Commerce.PaymentProviderRegistry>();
        services.AddSingleton<Commerce.ICheckoutBudget, Commerce.CheckoutBudget>();
        services.AddSingleton<Commerce.IPurchaseService, Commerce.PurchaseService>();
    }
}
