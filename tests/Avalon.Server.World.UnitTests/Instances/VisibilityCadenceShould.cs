using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auras;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.World;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #981 (the owner's decision): which objects a client is told exist is decided once per broadcast period, on its
/// instance's broadcast tick, so an object is listed or removed up to one period (six ticks, 100 ms) after it came into
/// range or left it. What a client is told keeps its order: an object is added before any update of it, its aura list
/// follows its add, it hears no aura change the tick it is listed nor while it is not listed, and it is listed again when
/// it comes back. A character that left is removed on the next tick, as before, and a client arriving is listed itself
/// and everything in view on its first tick.
/// </summary>
/// <remarks>
/// The instances here are never published, so the broadcast clock starts at 0; a tick of 1/60 s is 166,666 TimeSpan ticks,
/// so the clock first reaches the 100 ms interval on tick 7 and then every sixth tick: 7, 13, 19 and 25. Each change is
/// made right after a broadcast, before tick 8, the latest a change can come, so what it brings arrives exactly one
/// period later, on tick 13: the longest delay there can be. Ids 981_0xx (characters)
/// and 981_9xx (creatures) are unique to this class.
/// </remarks>
public class VisibilityCadenceShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);
    private const int LastTick = 25;
    private const int ChangeBefore = 8;
    private static readonly AuraId s_fortified = new(905);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    public enum Change
    {
        ComesIntoView,
        LeavesView,
        StaysInTheMargin,
        ComesBack,
        Despawns,
        CharacterLeaves,
        CharacterComesBackFromElsewhere,
        CharacterMovesWithinTheInstance,
    }

    /// <summary>
    /// What the watcher at the origin is told of the one object a change concerns, tick by tick: the boar (holding an aura,
    /// so it has a list) or the other player. Nothing arrives before the tick given, and nothing else at all.
    /// </summary>
    [Theory]
    [InlineData(Change.ComesIntoView, "13 add, 13 aura list")]
    [InlineData(Change.LeavesView, "13 remove")]
    [InlineData(Change.StaysInTheMargin, "13 update")]
    [InlineData(Change.ComesBack, "13 remove, 19 add, 19 aura list")]
    [InlineData(Change.Despawns, "13 remove")]
    [InlineData(Change.CharacterLeaves, "8 remove")]
    [InlineData(Change.CharacterComesBackFromElsewhere, "8 remove, 13 add")]
    [InlineData(Change.CharacterMovesWithinTheInstance, "")]
    public async Task Tell_a_client_of_what_comes_and_goes_on_the_broadcast_after_it(Change change, string expected)
    {
        using MapInstance instance = TestMapInstances.Build(await WorldAsync(), time: _clock);
        MapInstanceClient watcher = Player(instance, 981_001, 0f);
        MapInstanceClient other = Player(instance, 981_002, 5f);
        Creature boar = Boar(instance, 981_901, change == Change.ComesIntoView ? 200f : 10f);
        instance.Auras.Apply(boar, boar, s_fortified, AuraSource.None);
        ulong watched = change is Change.CharacterLeaves or Change.CharacterComesBackFromElsewhere
            or Change.CharacterMovesWithinTheInstance
            ? other.Character.Guid.RawValue
            : boar.Guid.RawValue;

        var told = new List<string>();
        for (int tick = 1; tick <= LastTick; tick++)
        {
            if (tick == ChangeBefore)
                Make(change, instance, other, boar);
            if (tick == ChangeBefore + 1 && change == Change.ComesIntoView)
                instance.Auras.Apply(boar, boar, s_fortified, AuraSource.None);   // a change it is not listed yet
            if (tick == ChangeBefore + 2 && change == Change.CharacterComesBackFromElsewhere)
                instance.AddCharacter(other.Connection);
            if (tick == ChangeBefore + 6 && change == Change.ComesBack)
                boar.Position = new Vector3(0f, 0f, 10f);

            int from = watcher.Sent.Count;
            instance.Update(s_tick);
            if (tick >= ChangeBefore)
                told.AddRange(ToldOf(watcher.Sent.Skip(from), watched).Select(kind => $"{tick} {kind}"));
        }

        Assert.Equal(expected, string.Join(", ", told));
    }

    /// <summary>
    /// A client entering mid-period is listed itself and everything in view on its own first tick, its own aura list and
    /// the boar's after the add, as it was on every tick before #981; the client already there lists it on the broadcast.
    /// A whole view round (#1181 Part D, <c>IAbilityArena.ViewRound</c>, which a pooled projectile's guid waits two of)
    /// counts only on the broadcast ticks, when every view is decided, never for the arriving client's own decision.
    /// </summary>
    [Fact]
    public async Task List_an_arriving_client_its_view_at_once_and_list_it_to_the_others_on_the_broadcast()
    {
        using MapInstance instance = TestMapInstances.Build(await WorldAsync(), time: _clock);
        MapInstanceClient watcher = Player(instance, 981_011, 0f);
        Creature boar = Boar(instance, 981_911, 10f);
        instance.Auras.Apply(boar, boar, s_fortified, AuraSource.None);
        for (int tick = 1; tick < ChangeBefore; tick++)
            instance.Update(s_tick);

        MapInstanceClient arriving = Player(instance, 981_012, 5f);
        var arrivingTold = new List<string>();
        var watcherTold = new List<string>();
        var rounds = new List<int>();
        for (int tick = ChangeBefore; tick <= LastTick; tick++)
        {
            int arrivingFrom = arriving.Sent.Count, watcherFrom = watcher.Sent.Count;
            uint round = ((IAbilityArena)instance).ViewRound;
            instance.Update(s_tick);
            if (((IAbilityArena)instance).ViewRound != round)
                rounds.Add(tick);
            foreach (ulong guid in new[] { arriving.Character.Guid.RawValue, watcher.Character.Guid.RawValue, boar.Guid.RawValue })
                arrivingTold.AddRange(ToldOf(arriving.Sent.Skip(arrivingFrom), guid).Select(kind => $"{tick} {Name(guid)} {kind}"));
            watcherTold.AddRange(ToldOf(watcher.Sent.Skip(watcherFrom), arriving.Character.Guid.RawValue).Select(kind => $"{tick} {kind}"));
        }

        Assert.Equal("8 self add, 8 self aura list, 8 watcher add, 8 boar add, 8 boar aura list",
            string.Join(", ", arrivingTold));
        Assert.Equal("13 add", string.Join(", ", watcherTold));
        Assert.Equal([13, 19, 25], rounds);

        string Name(ulong guid) => guid == boar.Guid.RawValue ? "boar"
            : guid == watcher.Character.Guid.RawValue ? "watcher" : "self";
    }

    /// <summary>
    /// A client whose state broadcast throws, on the broadcast tick that lists the boar, does not cost the client after it
    /// its add: each client's broadcast is contained. The one that threw is started over: told to drop all it was told of,
    /// and listed itself and its view in full on the next tick. Later, a tick that throws before the views are decided (a
    /// client's packets throwing, on the broadcast tick that would remove the boar) leaves the decision owed to the next
    /// tick, rather than to the next broadcast; that tick counts no view round (#1181 Part D), the next, which decides
    /// every view, does, and the started-over client's own decision on the tick after its broadcast threw counts none.
    /// </summary>
    [Fact]
    public async Task Tell_the_others_and_start_over_a_client_whose_broadcast_throws_and_decide_after_a_tick_that_throws()
    {
        using MapInstance instance = TestMapInstances.Build(await WorldAsync(), time: _clock);
        MapInstanceClient failing = Player(instance, 981_021, 0f);
        MapInstanceClient after = Player(instance, 981_022, 5f);
        Creature boar = Boar(instance, 981_921, 200f);
        instance.Auras.Apply(boar, boar, s_fortified, AuraSource.None);

        bool refuseNextAdd = false;
        failing.Connection.When(c => c.Send(Arg.Is<OutboundPacket>(p => p.Header.Type == NetworkPacketType.SMSG_WORLD_STATE_ADD)))
            .Do(call =>
            {
                if (!refuseNextAdd)
                    return;
                refuseNextAdd = false;
                failing.Sent.Remove(call.Arg<OutboundPacket>());
                throw new InvalidOperationException("The connection refused the packet.");
            });
        bool throwOnInput = false;
        failing.Connection.When(c => c.UpdateMap()).Do(_ =>
        {
            if (!throwOnInput)
                return;
            throwOnInput = false;
            throw new InvalidOperationException("The client's packets threw.");
        });

        ulong[] watched = [boar.Guid.RawValue, failing.Character.Guid.RawValue];
        var failingTold = new List<string>();
        var afterTold = new List<string>();
        var rounds = new List<int>();
        for (int tick = 1; tick <= 26; tick++)
        {
            uint round = ((IAbilityArena)instance).ViewRound;
            if (tick == ChangeBefore)
                boar.Position = new Vector3(0f, 0f, 10f);       // listed on the broadcast, tick 13
            if (tick == 13)
                refuseNextAdd = true;                           // that broadcast's add to the first client throws
            if (tick == 20)
                boar.Position = new Vector3(0f, 0f, 90f);       // removed on the broadcast, tick 25 ...
            if (tick == 25)
                throwOnInput = true;                            // ... which throws before the views are decided

            int failingFrom = failing.Sent.Count, afterFrom = after.Sent.Count;
            if (tick == 25)
                Assert.Throws<InvalidOperationException>(() => instance.Update(s_tick));
            else
                instance.Update(s_tick);
            if (((IAbilityArena)instance).ViewRound != round)
                rounds.Add(tick);

            if (tick < ChangeBefore)
                continue;
            foreach (ulong guid in watched)
            {
                string name = guid == boar.Guid.RawValue ? "boar" : "first";
                failingTold.AddRange(ToldOf(failing.Sent.Skip(failingFrom), guid).Select(kind => $"{tick} {name} {kind}"));
                afterTold.AddRange(ToldOf(after.Sent.Skip(afterFrom), guid).Select(kind => $"{tick} {name} {kind}"));
            }
        }

        Assert.Equal("13 boar add, 13 boar aura list, 26 boar remove", string.Join(", ", afterTold));
        Assert.Equal("13 boar remove, 13 first remove, 14 boar add, 14 boar aura list, 14 first add, 14 first aura list, "
                     + "26 boar remove", string.Join(", ", failingTold));
        Assert.Equal([7, 13, 19, 26], rounds);
    }

    private static void Make(Change change, MapInstance instance, MapInstanceClient other, Creature boar)
    {
        switch (change)
        {
            case Change.ComesIntoView:
            case Change.ComesBack:
                boar.Position = change == Change.ComesBack ? new Vector3(0f, 0f, 75f) : new Vector3(0f, 0f, 10f);
                break;
            case Change.LeavesView:
                boar.Position = new Vector3(0f, 0f, 75f);   // beyond 60 m and the 10 m margin
                break;
            case Change.StaysInTheMargin:
                boar.Position = new Vector3(0f, 0f, 65f);   // out of range, inside the margin: kept
                break;
            case Change.Despawns:
                instance.RemoveCreature(boar);
                break;
            case Change.CharacterLeaves:
            case Change.CharacterComesBackFromElsewhere:
                instance.RemoveCharacter(other.Connection);
                break;
            case Change.CharacterMovesWithinTheInstance:
                instance.RemoveCharacter(other.Connection);
                instance.AddCharacter(other.Connection);
                break;
        }
    }

    /// <summary>What these packets told their client of <paramref name="guid" />, in order.</summary>
    private static IEnumerable<string> ToldOf(IEnumerable<OutboundPacket> packets, ulong guid)
    {
        foreach (OutboundPacket packet in packets)
        {
            switch (packet.Header.Type)
            {
                case NetworkPacketType.SMSG_WORLD_STATE_ADD:
                    if (Decode<SInstanceStateAddPacket>(packet).Adds?.Any(s => s.Guid == guid) == true)
                        yield return "add";
                    break;
                case NetworkPacketType.SMSG_WORLD_STATE_UPDATE:
                    if (Decode<SInstanceStateUpdatePacket>(packet).Updates?.Any(s => s.Guid == guid) == true)
                        yield return "update";
                    break;
                case NetworkPacketType.SMSG_WORLD_STATE_REMOVE:
                    if (Decode<SInstanceStateRemovePacket>(packet).Removes?.Contains(guid) == true)
                        yield return "remove";
                    break;
                case NetworkPacketType.SMSG_AURA_LIST:
                    if (Decode<SAuraListPacket>(packet).UnitGuid == guid)
                        yield return "aura list";
                    break;
                case NetworkPacketType.SMSG_AURA_UPDATE:
                    if (Decode<SAuraUpdatePacket>(packet).UnitGuid == guid)
                        yield return "aura update";
                    break;
            }
        }
    }

    private static T Decode<T>(OutboundPacket packet)
    {
        using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
        return Serializer.Deserialize<T>(stream);
    }

    private async Task<IWorld> WorldAsync() => NewWorld(await TestStaticData.LoadAsync(TestStaticData.Repositories(
        auras: () => [AuraTestData.Fortified()])));

    private static MapInstanceClient Player(MapInstance instance, uint id, float z)
    {
        MapInstanceClient client = Join(instance, Inventory.TestCharacters.New(id));
        client.Character.Position = new Vector3(0f, 0f, z);
        return client;
    }

    private static Creature Boar(MapInstance instance, uint id, float z)
    {
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = new Vector3(0f, 0f, z),
            Level = 1,
            Health = 100,
            CurrentHealth = 100,
        };
        instance.AddCreature(boar);
        return boar;
    }
}
