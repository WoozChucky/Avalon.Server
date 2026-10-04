namespace Avalon.Configuration;

public sealed class SteamPlaytestConfiguration
{
    public bool Enabled { get; set; }
    public uint AppId { get; set; }
    public ushort[] AllowedWorldIds { get; set; } = [];
}

public sealed class SteamApplicationSelection(uint appId, bool restricted, IReadOnlyList<ushort> allowedWorldIds)
{
    public uint AppId { get; } = appId;
    public bool Restricted { get; } = restricted;
    public IReadOnlyList<ushort> AllowedWorldIds { get; } = Array.AsReadOnly(allowedWorldIds.ToArray());
    public bool AllowsWorld(ushort worldId) => worldId != 0 && (!Restricted || AllowedWorldIds.Contains(worldId));
}
