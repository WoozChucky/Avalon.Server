using Avalon.World.Parties;

namespace Avalon.World.Instances;

/// <summary>
/// The party side of the instance registry (2026-09-30). World-side, deliberately not on the modding API's
/// IInstanceRegistry.
/// </summary>
public interface IPartyInstanceRegistry
{
    /// <summary>Whether instance <paramref name="instanceId" /> is live and owned by <paramref name="party" />.</summary>
    bool IsPartyInstance(PartyId party, Guid instanceId);

    /// <summary>Drops the party's index entries, so no member is routed to its instances again. The instances empty and expire as usual.</summary>
    void ForgetParty(PartyId party);
}
