namespace Avalon.Api.Commerce;

public sealed class CommerceConfiguration
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = StripePaymentProvider.ProviderName;
    public string PaymentEnvironment { get; set; } = CommerceEnvironments.Sandbox;
    public string LicenseEnvironment { get; set; } = CommerceEnvironments.DevelopmentLicense;
    public long AmountMinor { get; set; } = 800;
    public string Currency { get; set; } = "eur";
    public int Quantity { get; set; } = CommercePolicy.GameLicenseQuantity;
    public string Product { get; set; } = Avalon.Configuration.StoreAuthenticationConfiguration.Product;
    public string ProviderProduct { get; set; } = Avalon.Configuration.StoreAuthenticationConfiguration.NativeProviderProduct;
    public string PublicSiteOrigin { get; set; } = "";
    public string OfferId { get; set; } = "";
    public string ProviderPriceId { get; set; } = "";
    public string ProviderCatalogProductId { get; set; } = "";
    public string ProviderAccountId { get; set; } = "";
    // Configuration binding appends array entries; the configured list must be exact.
    public string[] PaymentMethods { get; set; } = [];
    public string ApiKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}
