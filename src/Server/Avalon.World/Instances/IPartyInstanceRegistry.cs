using Avalon.Common.ValueObjects;
using Avalon.World.Parties;
using Avalon.World.Public.Instances;

namespace Avalon.World.Instances;

/// <summary>
/// The party side of the instance registry (2026-09-30). World-side, deliberately not on the modding API's
/// IInstanceRegistry.
/// </summary>
public interface IPartyInstanceRegistry
{
    /// <summary>
    /// The party's default instance of <paramref name="templateId" />, the last live entry of its list in default order
    /// (#1104), or a new one; members entering at once share one build. Not expired: an instance abandoned for longer than
    /// Game:AbandonedInstanceLifetimeMinutes (15 by default) is skipped, and with none live a new one is built.
    /// </summary>
    Task<IMapInstance> GetOrCreatePartyInstanceAsync(PartyId party, MapTemplateId templateId);

    /// <summary>Whether instance <paramref name="instanceId" /> is live and owned by <paramref name="party" />.</summary>
    bool IsPartyInstance(PartyId party, Guid instanceId);

    /// <summary>Drops the party's index entries, so no member is routed to its instances again. The instances empty and expire as usual.</summary>
    void ForgetParty(PartyId party);
}
