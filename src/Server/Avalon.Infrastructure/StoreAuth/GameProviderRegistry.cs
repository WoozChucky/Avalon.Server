namespace Avalon.Infrastructure.StoreAuth;

/// <summary>Only explicitly registered adapters can verify identity or license evidence.</summary>
public sealed class GameProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IGameIdentityProvider> identities;
    private readonly IReadOnlyDictionary<string, IGameLicenseProvider> licenses;
    public GameProviderRegistry(IEnumerable<IGameIdentityProvider> identities, IEnumerable<IGameLicenseProvider> licenses)
    {
        this.identities = identities.ToDictionary(x => Validate(x.Provider), StringComparer.Ordinal);
        this.licenses = licenses.ToDictionary(x => Validate(x.Provider), StringComparer.Ordinal);
    }
    public IGameIdentityProvider? Identity(string provider) => identities.GetValueOrDefault(provider);
    public IGameLicenseProvider? License(string provider) => licenses.GetValueOrDefault(provider);
    private static string Validate(string provider) => !string.IsNullOrWhiteSpace(provider) && provider == provider.Trim() && provider.Length <= 32
        ? provider : throw new ArgumentException("Invalid game provider registration.");
}
