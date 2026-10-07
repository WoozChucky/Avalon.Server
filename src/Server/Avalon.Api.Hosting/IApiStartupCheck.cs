namespace Avalon.Api.Hosting;

/// <summary>
/// A check that must pass before an API process serves, and cannot be an options validation because OpenAPI
/// generation starts the host, which runs those, with nothing configured: a store's publisher key, say. A service
/// registers its checks; <see cref="ApiStartup"/> runs every one, in registration order, after the options are
/// validated and Database:Worlds is read, and before any database call. OpenAPI generation skips them.
/// </summary>
public interface IApiStartupCheck
{
    /// <summary>Throws, naming the setting, when the check fails.</summary>
    void Check(IServiceProvider services);
}
