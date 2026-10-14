using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World;
using Avalon.World.Public.Combat;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Avalon.World.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #980: every player's state adds and updates are put together from entries each encoded once per broadcast, and must
/// be byte for byte the packets the per-recipient encoding wrote: the recipient's described list, encoded whole by
/// <see cref="SInstanceStateAddPacket.Create" /> or <see cref="SInstanceStateUpdatePacket.Create" />. Character ids
/// 980_1xx and creature ids 980_9xx are this class's.
/// </summary>
public class StateUpdateEncodingShould
{
    // 166,666 TimeSpan ticks, a hair under 1/60 s: an instance built outside the registry broadcasts on ticks 7, 13, 19, 25.
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    public static TheoryData<string> Broadcasts =>
        ["town", "forest", "self", "listed-and-changed", "out-of-view", "arrivals", "projectiles", "solo", "pair"];

    /// <summary>
    /// Real broadcasts through a real instance: a crowded town where everyone moves, everyone listed to everyone on its
    /// first tick; a forest of creatures moving and hurt at different distances; characters whose own update is health
    /// alone or motion alone (left out for themselves); a character listed to some recipients on the broadcast tick it
    /// changed and updated to the others, then listed again after leaving their view; a recipient too far to see
    /// anyone; creatures walking into the view of several recipients on one tick, out and back in, with a character
    /// arriving midway (its own add, and everyone listed to it); and projectiles cast through the real cast system,
    /// one flying into a recipient's view mid-flight and one finishing in flight; and a solo player and a pair among
    /// creatures that move, are hurt and come into and out of view, the first encoded whole (fewer recipients than
    /// <see cref="StatePacketAssembler.MinRecipientsToShare" />) and the second from shared entries. Every recipient's add
    /// and update of every tick, or their absence, is compared.
    /// </summary>
    [Theory]
    [MemberData(nameof(Broadcasts))]
    public void Be_byte_for_byte_the_per_recipient_encoding(string broadcast)
    {
        using MapInstance instance = Casting(out CastAbilityHandler handler);
        var characters = new List<MapInstanceClient>();
        var creatures = new List<Creature>();
        Action<int> change = Setup(broadcast, instance, handler, characters, creatures);
        var encoder = new PacketEncoder(new PayloadSegmentPool());
        int adds = 0;
        int updates = 0;

        for (int tick = 1; tick <= 25; tick++)
        {
            foreach (MapInstanceClient client in characters)
                client.Sent.Clear();
            change(tick);
            instance.Update(s_tick);

            foreach (MapInstanceClient recipient in characters)
            {
                adds += Compare(recipient, NetworkPacketType.SMSG_WORLD_STATE_ADD, SInstanceStateAddPacket.Flags,
                    PerRecipientAdds(recipient.Character, instance, characters, creatures, encoder));
                updates += Compare(recipient, NetworkPacketType.SMSG_WORLD_STATE_UPDATE, SInstanceStateUpdatePacket.Flags,
                    PerRecipientUpdates(recipient.Character, instance, characters, creatures, encoder));
            }
        }

        Assert.True(adds > 0 && updates > 0, $"Compared {adds} adds and {updates} updates");
    }

    /// <summary>
    /// An entity asked for with two field sets in one broadcast gets an entry for each, never one written for the other:
    /// unreachable while every recipient is sent an entity with the fields its snapshot marked, but what a way of
    /// finding watchers that tracks fields per recipient (#981, #982) would rely on. Three recipients ask for one
    /// character, with motion, with motion and health, then with motion again; each packet is compared with its entry
    /// encoded whole by <see cref="SInstanceStateUpdatePacket.Create" />.
    /// </summary>
    [Fact]
    public void Keep_an_entry_for_each_field_set_an_entity_is_asked_for()
    {
        CharacterEntity character = Inventory.TestCharacters.New(980_190);
        character.Position = new Vector3(4f, 0f, 2f);
        character.Velocity = new Vector3(1f, 0f, 0f);
        character.CurrentHealth = 42;
        const GameEntityFields Motion = GameEntityFields.Position | GameEntityFields.Velocity;
        const GameEntityFields Hurt = Motion | GameEntityFields.CurrentHealth;
        var encoder = new PacketEncoder(new PayloadSegmentPool());
        var updates = StatePacketAssembler.ForUpdates();
        updates.BeginBroadcast();

        foreach (GameEntityFields fields in new[] { Motion, Hurt, Motion })
        {
            updates.BeginPacket();
            if (!updates.TryAppendShared(character.Guid, fields))
                updates.AppendShared(character.Guid, fields, ObjectStateWriter.From((ICharacter)character, fields));

            OutboundPacket assembled = updates.Finish(encoder);
            OutboundPacket whole = SInstanceStateUpdatePacket.Create(
                [ObjectStateWriter.From((ICharacter)character, fields)], encoder);
            Assert.Equal(whole.PayloadMemory.ToArray(), assembled.PayloadMemory.ToArray());
            assembled.Release();
            whole.Release();
        }
    }

    /// <summary>The one packet of <paramref name="type" /> the recipient was sent this tick against the expected bytes, or none against null.</summary>
    private static int Compare(MapInstanceClient recipient, NetworkPacketType type, NetworkPacketFlags flags, byte[]? expected)
    {
        OutboundPacket[] sent = recipient.Sent.Where(p => p.Header.Type == type).ToArray();
        if (expected is null)
        {
            Assert.Empty(sent);
            return 0;
        }

        OutboundPacket packet = Assert.Single(sent);
        Assert.Equal(flags, packet.Header.Flags);
        Assert.Equal(expected, packet.PayloadMemory.ToArray());
        return 1;
    }

    /// <summary>Who is where, and what changes before each tick.</summary>
    private static Action<int> Setup(string broadcast, MapInstance instance, CastAbilityHandler handler,
        List<MapInstanceClient> characters, List<Creature> creatures)
    {
        switch (broadcast)
        {
            case "town":
                for (uint i = 0; i < 12; i++)
                    characters.Add(JoinAt(instance, 980_100 + i, new Vector3(i % 4, 0f, i % 3)));
                return tick =>
                {
                    for (int i = 0; i < characters.Count; i++)
                    {
                        CharacterEntity character = characters[i].Character;
                        character.Position = new Vector3(i % 4 + tick * 0.01f, 0f, i % 3);
                        character.Velocity = new Vector3(tick % 2, 0f, 0f);
                        if ((tick + i) % 5 == 0)
                            character.CurrentHealth = (uint)(50 + i + tick);
                    }
                };

            case "forest":
                characters.Add(JoinAt(instance, 980_120, Vector3.zero));
                characters.Add(JoinAt(instance, 980_121, new Vector3(40f, 0f, 0f)));
                characters.Add(JoinAt(instance, 980_122, new Vector3(110f, 0f, 0f)));
                for (uint i = 0; i < 15; i++)
                    creatures.Add(AddCreature(instance, 980_900 + i, new Vector3(i * 10f, 0f, 5f)));
                return tick =>
                {
                    for (int i = 0; i < creatures.Count; i++)
                    {
                        if ((i + tick) % 3 == 0)
                            creatures[i].Position = new Vector3(i * 10f + tick * 0.2f, 0f, 5f);
                        if ((i + tick) % 7 == 0)
                            creatures[i].CurrentHealth = (uint)(90 - tick);
                    }

                    characters[1].Character.Position = new Vector3(40f + tick, 0f, 0f);
                };

            case "self":
                characters.Add(JoinAt(instance, 980_130, Vector3.zero));
                characters.Add(JoinAt(instance, 980_131, new Vector3(3f, 0f, 0f)));
                characters.Add(JoinAt(instance, 980_132, new Vector3(0f, 0f, 3f)));
                return tick =>
                {
                    characters[0].Character.CurrentHealth = (uint)(80 - tick);   // health alone: its own client too
                    characters[1].Character.Position = new Vector3(3f + tick * 0.1f, 0f, 0f);   // motion alone
                    if (tick % 2 == 0)
                    {
                        characters[2].Character.Position = new Vector3(0f, 0f, 3f + tick * 0.1f);   // both
                        characters[2].Character.CurrentHealth = (uint)(70 - tick);
                    }
                };

            case "listed-and-changed":
                characters.Add(JoinAt(instance, 980_140, Vector3.zero));
                characters.Add(JoinAt(instance, 980_141, new Vector3(100f, 0f, 0f)));
                characters.Add(JoinAt(instance, 980_142, new Vector3(110f, 0f, 0f)));
                creatures.Add(AddCreature(instance, 980_940, new Vector3(100f, 0f, 10f)));
                return tick =>
                {
                    // On broadcast ticks 7, 13 and 19 the walker crosses into one character's view as it moves and is
                    // hurt: listed to that one, updated to those that already saw it.
                    MapInstanceClient walker = characters[1];
                    if (tick is 7 or 13 or 19)
                    {
                        walker.Character.Position = new Vector3(tick == 13 ? 100f : 50f, 0f, 0f);
                        walker.Character.CurrentHealth = (uint)(90 - tick);
                        creatures[0].Position = new Vector3(tick == 13 ? 100f : 55f, 0f, 10f);
                    }
                };

            case "out-of-view":
                characters.Add(JoinAt(instance, 980_150, Vector3.zero));
                characters.Add(JoinAt(instance, 980_151, new Vector3(2f, 0f, 0f)));
                characters.Add(JoinAt(instance, 980_152, new Vector3(500f, 0f, 500f)));
                creatures.Add(AddCreature(instance, 980_950, new Vector3(5f, 0f, 0f)));
                return tick =>
                {
                    characters[1].Character.Position = new Vector3(2f + tick * 0.1f, 0f, 0f);
                    creatures[0].CurrentHealth = (uint)(95 - tick);
                };

            case "arrivals":
                characters.Add(JoinAt(instance, 980_160, Vector3.zero));
                characters.Add(JoinAt(instance, 980_161, new Vector3(3f, 0f, 0f)));
                characters.Add(JoinAt(instance, 980_162, new Vector3(0f, 0f, 3f)));
                for (uint i = 0; i < 3; i++)
                    creatures.Add(AddCreature(instance, 980_960 + i, new Vector3(200f, 0f, i * 2f)));
                return tick =>
                {
                    // Listed to all three on tick 4, out of everyone's view on tick 10, listed again on tick 16.
                    if (tick is 4 or 10 or 16)
                    {
                        for (int i = 0; i < creatures.Count; i++)
                        {
                            creatures[i].Position = new Vector3(tick == 10 ? 200f : 5f, 0f, i * 2f);
                            creatures[i].CurrentHealth = (uint)(90 - tick - i);
                        }
                    }

                    if (tick == 8)
                        characters.Add(JoinAt(instance, 980_163, new Vector3(1f, 0f, 1f)));
                };

            case "projectiles":
                characters.Add(JoinAt(instance, 980_180, Vector3.zero));
                characters.Add(JoinAt(instance, 980_181, new Vector3(5f, 0f, 20f)));
                characters.Add(JoinAt(instance, 980_182, new Vector3(0f, 0f, 66f)));   // the first comes into view
                characters.Add(JoinAt(instance, 980_183, new Vector3(500f, 0f, 0f)));   // sees none
                return tick =>
                {
                    // At 20 m/s: the first flies on past tick 25 and into the third character's view around tick 20; the
                    // second character's finishes 3 m out around tick 14, and its final state goes out on the next
                    // broadcast. The second is cast by another character: the first's global cooldown still runs.
                    if (tick == 2)
                        CastProjectile(handler, characters[0], new Vector3(0f, 0f, 90f), reach: 90f);
                    if (tick == 5)
                        CastProjectile(handler, characters[1], new Vector3(15f, 0f, 20f), reach: 3f);
                };

            case "solo":
            case "pair":
                characters.Add(JoinAt(instance, broadcast == "solo" ? 980_200u : 980_210u, Vector3.zero));
                if (broadcast == "pair")
                    characters.Add(JoinAt(instance, 980_211, new Vector3(2f, 0f, 2f)));
                for (uint i = 0; i < 6; i++)
                {
                    creatures.Add(AddCreature(instance, (broadcast == "solo" ? 980_970u : 980_980u) + i,
                        new Vector3(10f + i * 5f, 0f, 4f)));
                }

                return tick =>
                {
                    // Its own update: motion, left out for itself, and health.
                    characters[0].Character.Position = new Vector3(tick * 0.05f, 0f, 0f);
                    characters[0].Character.CurrentHealth = (uint)(90 - tick);
                    for (int i = 0; i < 5; i++)
                    {
                        if ((i + tick) % 2 == 0)
                            creatures[i].Position = new Vector3(10f + i * 5f + tick * 0.1f, 0f, 4f);
                        if ((i + tick) % 5 == 0)
                            creatures[i].CurrentHealth = (uint)(95 - tick);
                    }

                    // The last walks out of view on tick 10 and back in on tick 16: removed, then listed again.
                    if (tick is 10 or 16)
                        creatures[5].Position = new Vector3(tick == 10 ? 200f : 30f, 0f, 4f);
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(broadcast), broadcast, null);
        }
    }

    /// <summary>
    /// The add the per-recipient encoding sent <paramref name="recipient" /> on the tick just run, or null for none:
    /// every entity its tracking reported new, described in full as the instance describes it for this recipient (a
    /// character's motion left out for itself), the list encoded whole.
    /// </summary>
    private static byte[]? PerRecipientAdds(CharacterEntity recipient, MapInstance instance,
        List<MapInstanceClient> characters, List<Creature> creatures, PacketEncoder encoder)
    {
        var adds = new List<ObjectState>();
        foreach (ObjectGuid guid in recipient.CharacterGameState.NewObjects)
        {
            if (characters.Find(c => c.Character.Guid == guid) is { } character)
            {
                adds.Add(ObjectStateWriter.From((ICharacter)character.Character,
                    MapInstance.MaskSelfSuppression(GameEntityFields.All, guid, recipient.Guid)));
            }
            else if (guid.Type == ObjectType.SpellProjectile)
            {
                adds.Add(ObjectStateWriter.From((IWorldObject)Projectile(instance, guid)));
            }
            else
            {
                adds.Add(ObjectStateWriter.From((ICreature)creatures.Single(c => c.Guid == guid), GameEntityFields.All));
            }
        }

        if (adds.Count == 0)
            return null;

        OutboundPacket packet = SInstanceStateAddPacket.Create(adds, encoder);
        byte[] bytes = packet.PayloadMemory.ToArray();
        packet.Release();
        return bytes;
    }

    /// <summary>
    /// The update the per-recipient encoding sent <paramref name="recipient" /> on the tick just run, or null for none:
    /// every entity its tracking reported updated, described as the instance describes it for this recipient (a
    /// character's motion left out for itself, a character left with nothing dropped, a creature's routine selection
    /// plus its death and maximum health when marked), the list encoded whole.
    /// </summary>
    private static byte[]? PerRecipientUpdates(CharacterEntity recipient, MapInstance instance,
        List<MapInstanceClient> characters, List<Creature> creatures, PacketEncoder encoder)
    {
        var updates = new List<ObjectState>();
        foreach ((ObjectGuid guid, GameEntityFields fields) in recipient.CharacterGameState.UpdatedObjects)
        {
            if (characters.Find(c => c.Character.Guid == guid) is { } character)
            {
                GameEntityFields own = MapInstance.MaskSelfSuppression(fields, guid, recipient.Guid);
                if ((own & ~GameEntityFields.None) != 0)
                    updates.Add(ObjectStateWriter.From((ICharacter)character.Character, own));
            }
            else if (guid.Type == ObjectType.SpellProjectile)
            {
                updates.Add(ObjectStateWriter.From((IWorldObject)Projectile(instance, guid), fields));
            }
            else
            {
                ICreature creature = creatures.Single(c => c.Guid == guid);
                updates.Add(ObjectStateWriter.From(creature,
                    GameEntityFields.CreatureUpdate | (fields & (GameEntityFields.IsDead | GameEntityFields.Health))));
            }
        }

        if (updates.Count == 0)
            return null;

        OutboundPacket packet = SInstanceStateUpdatePacket.Create(updates, encoder);
        byte[] bytes = packet.PayloadMemory.ToArray();
        packet.Release();
        return bytes;
    }

    private static FlyingProjectileScript Projectile(MapInstance instance, ObjectGuid guid) =>
        FlyingProjectileScript.In(instance).Single(p => p.Guid == guid);

    /// <summary>
    /// An instance the real cast handler reaches, whose projectiles are <see cref="FlyingProjectileScript" />s, over a
    /// navigator that lets every ray through.
    /// </summary>
    private static MapInstance Casting(out CastAbilityHandler handler)
    {
        IScriptManager scripts = Substitute.For<IScriptManager>();
        scripts.GetAbilityScript("ProjectileAbilityScript").Returns(typeof(FlyingProjectileScript));
        IMapNavigator navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(default, default).ReturnsForAnyArgs(ci => ci.ArgAt<Vector3>(1));
        IWorld world = NewWorld();
        MapInstance instance = TestMapInstances.Build(world, scripts, navigator);
        world.InstanceRegistry.GetInstanceById(instance.InstanceId).Returns(instance);
        handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig(),
            TimeProvider.System);
        return instance;
    }

    /// <summary>Casts a 20 m/s projectile from <paramref name="caster" /> toward <paramref name="towards" />.</summary>
    private static void CastProjectile(CastAbilityHandler handler, MapInstanceClient caster, Vector3 towards, float reach)
    {
        caster.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Projectile(210, reach: reach, speed: 20f))]);
        handler.Execute(caster.Connection, new CCastAbilityPacket
        {
            AbilityId = 210,
            GroundPos = new Vector3Dto { X = towards.x, Y = towards.y, Z = towards.z },
        });
        Assert.Empty(caster.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
    }

    private static MapInstanceClient JoinAt(MapInstance instance, uint id, Vector3 position)
    {
        MapInstanceClient client = Join(instance, id);
        client.Character.Position = position;
        return client;
    }

    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = 100,
            CurrentHealth = 100,
        };
        instance.AddCreature(creature);
        return creature;
    }
}
