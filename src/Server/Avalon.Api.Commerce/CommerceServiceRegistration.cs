using Microsoft.Extensions.Options;
using Stripe;

namespace Avalon.Api.Commerce;

public static class CommerceServiceRegistration
{
    public static void AddCommerce(this IServiceCollection services)
    {
        services.AddOptions<CommerceConfiguration>().BindConfiguration("Application:Commerce").ValidateOnStart();
        services.AddSingleton<IValidateOptions<CommerceConfiguration>, CommerceOptionsValidator>();
        // Checkout URLs, buyer email and financial bindings must never enter EF parameter diagnostics.
        services.AddSingleton<IPostConfigureOptions<Avalon.Configuration.DatabaseConfiguration>>(sp =>
            new PostConfigureOptions<Avalon.Configuration.DatabaseConfiguration>(null, database =>
            {
                if (sp.GetRequiredService<IOptions<CommerceConfiguration>>().Value.Enabled)
                    database.EnableSensitiveDataLogging = false;
            }));
        services.AddSingleton(StripePaymentProvider.Registration);
#pragma warning disable EXTEXP0001 // Durable operation keys own replay; transport retries and body-bearing diagnostics are suppressed.
        services.AddHttpClient("avalon-commerce", http =>
            {
                http.Timeout = TimeSpan.FromSeconds(20);
                http.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            })
            .RemoveAllLoggers().RemoveAllResilienceHandlers()
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) => { handlers.Clear(); handlers.Add(new PaymentSecretProtectionHandler()); })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                MaxConnectionsPerServer = 16,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ActivityHeadersPropagator = null,
            });
#pragma warning restore EXTEXP0001
        services.AddSingleton(sp =>
        {
            CommerceConfiguration config = sp.GetRequiredService<IOptions<CommerceConfiguration>>().Value;
            return new StripeClient(config.Enabled ? config.ApiKey : "sk_test_disabled",
                httpClient: new SystemNetHttpClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("avalon-commerce"), maxNetworkRetries: 0, enableTelemetry: false),
                apiBase: "https://api.stripe.com");
        });
        services.AddSingleton<IPaymentProvider, StripePaymentProvider>();
        services.AddSingleton<PaymentProviderRegistry>();
        services.AddSingleton<ICheckoutBudget, CheckoutBudget>();
        services.AddSingleton<IPurchaseService, PurchaseService>();
        services.AddSingleton<IPaymentNotificationService, PaymentNotificationService>();
        services.AddSingleton<IPaymentReconciliationService, PaymentReconciliationService>();
        services.AddSingleton<IPurchaseAdministrationService, PurchaseAdministrationService>();
        if (!string.Equals(Environment.GetEnvironmentVariable("AVALON_OPENAPI_GENERATION_ONLY"), "true", StringComparison.OrdinalIgnoreCase))
            services.AddHostedService<PaymentReconciliationWorker>();
    }
}
