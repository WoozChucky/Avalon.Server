namespace Avalon.Infrastructure.StoreAuth;

/// <summary>Only explicitly registered adapters can verify identity or license evidence.</summary>
public sealed class GameProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IGameIdentityProvider> _identities;
    private readonly IReadOnlyDictionary<string, IGameLicenseProvider> _licenses;
    public GameProviderRegistry(IEnumerable<IGameIdentityProvider> identities, IEnumerable<IGameLicenseProvider> licenses)
    {
        _identities = identities.ToDictionary(x => Validate(x.Provider), StringComparer.Ordinal);
        _licenses = licenses.ToDictionary(x => Validate(x.Provider), StringComparer.Ordinal);
    }
    public IGameIdentityProvider? Identity(string provider) => _identities.GetValueOrDefault(provider);
    public IGameLicenseProvider? License(string provider) => _licenses.GetValueOrDefault(provider);
    private static string Validate(string provider) => !string.IsNullOrWhiteSpace(provider) && provider == provider.Trim() && provider.Length <= 32
        ? provider : throw new ArgumentException("Invalid game provider registration.");
}
