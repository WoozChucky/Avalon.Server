namespace Avalon.Api.Commerce;

public static class CommercePolicy
{
    public const int GameLicenseQuantity = 1;
    public const int MaximumNotificationBytes = 256 * 1024;
    public const int NotificationClockSkewSeconds = 300;
    public static readonly TimeSpan CheckoutLifetime = TimeSpan.FromMinutes(30);
}

public static class CommerceEnvironments
{
    public const string Sandbox = "sandbox";
    public const string DevelopmentLicense = "development";
    public const string ProductionLicense = "production";
    public const string DevelopmentIdentityPrefix = "avalon-auth-dev";
}
