namespace Avalon.Api.Config;

/// <summary>
/// How Program.cs gets its configuration, kept here so tests can pin the source order (#482).
/// </summary>
public static class ApiConfiguration
{
    /// <summary>
    /// The builder's configuration as <see cref="WebApplication.CreateBuilder(string[])"/> layered it:
    /// appsettings.json, appsettings.{Environment}.json, user-secrets (Development), environment
    /// variables, the command line, each overriding the one before. Nothing is added on top: adding
    /// appsettings.json again would override user-secrets and the environment, which is where the JWT
    /// signing key lives.
    /// </summary>
    public static IConfiguration Sources(WebApplicationBuilder builder) => builder.Configuration;

    /// <summary>The "Application" section, bound as the rest of the api reads it.</summary>
    public static ApplicationConfig Bind(IConfiguration configuration)
    {
        ApplicationConfig applicationConfig = new();
        configuration.Bind("Application", applicationConfig);
        return applicationConfig;
    }
}
