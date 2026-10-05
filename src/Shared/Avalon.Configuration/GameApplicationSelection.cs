namespace Avalon.Configuration;

/// <summary>Application binding resolved from deployment configuration, never from provider evidence.</summary>
public sealed class GameApplicationSelection
{
    public GameApplicationSelection(string key, string provider, string providerProductId, string product,
        string environment, IReadOnlyList<ushort> allowedWorldIds, bool restricted)
    {
        Key = key; Provider = provider; ProviderProductId = providerProductId; Product = product;
        Environment = environment; AllowedWorldIds = Array.AsReadOnly(allowedWorldIds.ToArray()); Restricted = restricted;
    }
    public string Key { get; }
    public string Provider { get; }
    public string ProviderProductId { get; }
    public string Product { get; }
    public string Environment { get; }
    public IReadOnlyList<ushort> AllowedWorldIds { get; }
    public bool Restricted { get; }
    public bool AllowsWorld(ushort worldId) => worldId > 0 && (!Restricted || AllowedWorldIds.Contains(worldId));
}

public sealed class GameProviderApplicationConfiguration
{
    public string Provider { get; set; } = string.Empty;
    public string ProviderProductId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool Restricted { get; set; }
    public ushort[] AllowedWorldIds { get; set; } = Array.Empty<ushort>();
}
