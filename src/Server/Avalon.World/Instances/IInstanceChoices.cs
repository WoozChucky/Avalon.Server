using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.World;
using Avalon.World.Parties;
using Avalon.World.Public.Instances;

namespace Avalon.World.Instances;

/// <summary>One instance a character may choose at a portal, for the menu (#1104).</summary>
public readonly record struct InstanceChoice(IMapInstance Instance, InstanceKind Kind, bool IsDefault);

/// <summary>
/// The registry's side for choosing an instance at a portal (#1104): an owner, a character or a party, holds several
/// live instances of a map, one of them the default a plain entry takes. World-side, deliberately not on the modding
/// API's IInstanceRegistry. Tick thread only.
/// </summary>
public interface IInstanceChoices
{
    /// <summary>Builds a new private instance of the map for the character: never shared with another request's
    /// build, listed for the character once published, and not its default until <see cref="MakeDefault" />.</summary>
    Task<IMapInstance> CreatePrivateInstanceAsync(uint characterId, MapTemplateId templateId);

    /// <summary>The same for a party: listed for the party once published, its default only after <see cref="MakeDefault" />.
    /// A build still running when the party is forgotten is published unlisted, as a default party build is.</summary>
    Task<IMapInstance> CreatePartyInstanceAsync(PartyId party, MapTemplateId templateId);

    /// <summary>The live, unexpired instance <paramref name="instanceId" /> of the map if it is the character's own
    /// private one or one of <paramref name="party" />'s; otherwise null.</summary>
    IMapInstance? FindChoosable(uint characterId, PartyId? party, MapTemplateId templateId, Guid instanceId);

    /// <summary>Appends to <paramref name="into" /> the character's live private instances of the map and, with a
    /// party, the party's, each flagged whether it is the default a plain entry would take. Reads only those owners' lists.</summary>
    void ListChoosable(uint characterId, PartyId? party, MapTemplateId templateId, List<InstanceChoice> into);

    /// <summary>Makes the instance its owner's default for its map (the character's for a private one, the party's
    /// for a party one); nothing for an instance no longer listed (expired, or its party forgotten).</summary>
    void MakeDefault(IMapInstance instance);
}
