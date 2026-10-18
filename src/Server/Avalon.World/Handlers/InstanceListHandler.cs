using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.World;
using Avalon.World.Instances;
using Avalon.World.Parties;
using Avalon.World.Public;
using Avalon.World.Public.Enums;

namespace Avalon.World.Handlers;

/// <summary>
/// The portal's menu (<c>CMSG_INSTANCE_LIST</c>, #1104): which live instances of the portal's target map the character
/// may enter, its own private ones and, in a party, the party's, answered with <see cref="SInstanceListPacket" />.
/// </summary>
/// <remarks>
/// <para>
/// The portal is asked what a portal entry asks of it (<see cref="PortalChecks" />), and a refusal is answered with the
/// result the entry would get and no instances (<see cref="SInstanceListPacket.CreateFailure" />); a town, which has
/// nothing to choose, is answered <c>NotInstanced</c>. The menu moves nothing, so a move under way does not refuse it,
/// and a refusal is not a refused portal entry: nothing is counted in <c>world.portal_entry.refusals</c>. Like an entry,
/// a request from a dead character is dropped with no reply.
/// </para>
/// <para>
/// Each entry carries its kind, whether someone is inside (no timer runs then), the time left before an empty one
/// expires on the world's clock (<c>Game:AbandonedInstanceLifetimeMinutes</c> from when it was emptied once entered, the
/// fixed <see cref="InstanceRegistry.UnenteredInstanceLifetime" /> from its build while nobody has entered it), and
/// whether a plain entry takes it. The default comes first, then the party's instances, then the private ones, each in
/// use first and then by most time left.
/// </para>
/// <para>
/// A client may send the request at will: it reads only the character's and its party's instances
/// (<see cref="IInstanceChoices.ListChoosable" />), and the lists it fills are the handler's own, kept from one request
/// to the next. The world builds one handler of each type and runs it on the tick alone, so they are never shared. A
/// request allocates only what its encoding needs.
/// </para>
/// </remarks>
[PacketHandler(NetworkPacketType.CMSG_INSTANCE_LIST)]
public class InstanceListHandler(IWorld world, TimeProvider time, PartyService? parties = null)
    : WorldPacketHandler<CInstanceListPacket>
{
    /// <summary>
    /// The menu's order: the default first, then the party's, then the private ones; within each, the ones in use, whose
    /// time does not run, then the most time left; the id last, so an answer's order never depends on the sort.
    /// </summary>
    private static readonly Comparison<InstanceChoiceDto> s_menuOrder = static (a, b) =>
    {
        if (a.IsDefault != b.IsDefault)
            return a.IsDefault ? -1 : 1;

        if (a.Kind != b.Kind)
            return a.Kind == InstanceKind.Party ? -1 : 1;

        int byTimeLeft = TimeLeft(b).CompareTo(TimeLeft(a));
        return byTimeLeft != 0 ? byTimeLeft : a.InstanceId.CompareTo(b.InstanceId);
    };

    // What the registry lists, then the entries sent, drawn from the DTOs every earlier request has made: emptied once
    // the answer is encoded, so no instance is kept alive by them.
    private readonly List<InstanceChoice> _choices = [];
    private readonly List<InstanceChoiceDto> _entries = [];
    private readonly List<InstanceChoiceDto> _dtos = [];

    public override void Execute(IWorldConnection connection, CInstanceListPacket packet)
    {
        if (!connection.InGame || connection.Character is not { } character || character.IsDead)
        {
            return;
        }

        ushort targetMap = packet.TargetMapId;
        PortalCheck portal = PortalChecks.Check(world, character, targetMap);
        if (portal.Refusal != PortalRefusal.None)
        {
            connection.Send(SInstanceListPacket.CreateFailure(targetMap, portal.Result, PacketEncoder.Shared));
            return;
        }

        MapTemplate targetTemplate = portal.Template!;
        if (targetTemplate.MapType != MapType.Normal)
        {
            connection.Send(SInstanceListPacket.CreateFailure(targetMap, MapTransitionResult.NotInstanced,
                PacketEncoder.Shared));
            return;
        }

        uint characterId = character.Guid.Id;
        Party? party = parties?.PartyOf(characterId);
        try
        {
            world.InstanceChoices.ListChoosable(characterId, party?.Id, targetTemplate.Id, _choices);
            Fill(_choices);
            _entries.Sort(s_menuOrder);
            connection.Send(SInstanceListPacket.Create(targetMap, party is not null,
                party?.IsLeader(characterId) == true, _entries, PacketEncoder.Shared));
        }
        finally
        {
            _choices.Clear();
            _entries.Clear();
        }
    }

    /// <summary>One entry per choice, in <see cref="_entries" />, each a DTO kept from an earlier request where there is one.</summary>
    private void Fill(List<InstanceChoice> choices)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;
        var abandonedLifetime = TimeSpan.FromMinutes(world.Configuration.AbandonedInstanceLifetimeMinutes);
        for (int i = 0; i < choices.Count; i++)
        {
            if (i == _dtos.Count)
                _dtos.Add(new InstanceChoiceDto());

            InstanceChoiceDto entry = _dtos[i];
            InstanceChoice choice = choices[i];
            entry.InstanceId = choice.Instance.InstanceId;
            entry.Kind = choice.Kind;
            entry.InUse = choice.Instance.PlayerCount > 0;
            entry.ExpiresInMs = entry.InUse ? 0u : ExpiresInMs(choice.Instance as MapInstance, abandonedLifetime, now);
            entry.IsDefault = choice.IsDefault;
            _entries.Add(entry);
        }
    }

    /// <summary>
    /// The time an empty instance has left, in milliseconds, floored at 0 and clamped to <see cref="uint" />: its
    /// lifetime as the registry reckons it, from when it was last emptied (from its build while nobody has entered it).
    /// </summary>
    private static uint ExpiresInMs(MapInstance? instance, TimeSpan abandonedLifetime, DateTime now)
    {
        // Never from the registry: it lists MapInstances only, and an empty one always has a LastEmptyAt.
        if (instance?.LastEmptyAt is not { } emptiedAt)
            return 0u;

        TimeSpan lifetime = instance.HasBeenEntered ? abandonedLifetime : InstanceRegistry.UnenteredInstanceLifetime;
        double left = (lifetime - (now - emptiedAt)).TotalMilliseconds;
        return left <= 0d ? 0u : left >= uint.MaxValue ? uint.MaxValue : (uint)left;
    }

    /// <summary>For the order: an instance in use has more time than any, since its timer does not run.</summary>
    private static long TimeLeft(InstanceChoiceDto entry) => entry.InUse ? long.MaxValue : entry.ExpiresInMs;
}
