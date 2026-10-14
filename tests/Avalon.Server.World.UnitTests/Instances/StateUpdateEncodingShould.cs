using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Serialization;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #980: every player's state update is put together from entries each encoded once per broadcast, and must be byte for
/// byte the packet the per-recipient encoding wrote: the recipient's described list, encoded whole by
/// <see cref="SInstanceStateUpdatePacket.Create" />. Character ids 980_1xx and creature ids 980_9xx are this class's.
/// </summary>
public class StateUpdateEncodingShould
{
    // 166,666 TimeSpan ticks, a hair under 1/60 s: an instance built outside the registry broadcasts on ticks 7, 13, 19, 25.
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    public static TheoryData<string> Broadcasts => ["town", "forest", "self", "listed-and-changed", "out-of-view"];

    /// <summary>
    /// Real broadcasts through a real instance: a crowded town where everyone moves, a forest of creatures moving and
    /// hurt at different distances, characters whose own update is health alone or motion alone (left out for
    /// themselves), a character listed to some recipients on the broadcast tick it changed and updated to the others,
    /// and a recipient too far to see anyone. Every recipient's packet of every broadcast tick, or its absence, is
    /// compared.
    /// </summary>
    [Theory]
    [MemberData(nameof(Broadcasts))]
    public void Be_byte_for_byte_the_per_recipient_encoding(string broadcast)
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        var characters = new List<MapInstanceClient>();
        var creatures = new List<Creature>();
        Action<int> change = Setup(broadcast, instance, characters, creatures);
        var encoder = new PacketEncoder(new PayloadSegmentPool());
        int compared = 0;

        for (int tick = 1; tick <= 25; tick++)
        {
            foreach (MapInstanceClient client in characters)
                client.Sent.Clear();
            change(tick);
            instance.Update(s_tick);

            foreach (MapInstanceClient recipient in characters)
            {
                byte[]? expected = PerRecipientEncoding(recipient.Character, characters, creatures, encoder);
                OutboundPacket[] sent = recipient.Sent
                    .Where(p => p.Header.Type == NetworkPacketType.SMSG_WORLD_STATE_UPDATE).ToArray();
                if (expected is null)
                {
                    Assert.Empty(sent);
                    continue;
                }

                OutboundPacket packet = Assert.Single(sent);
                Assert.Equal(SInstanceStateUpdatePacket.Flags, packet.Header.Flags);
                Assert.Equal(expected, packet.PayloadMemory.ToArray());
                compared++;
            }
        }

        Assert.True(compared > 0, "No state update was compared");
    }

    /// <summary>Who is where, and what changes before each tick.</summary>
    private static Action<int> Setup(string broadcast, MapInstance instance, List<MapInstanceClient> characters,
        List<Creature> creatures)
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

            default:
                throw new ArgumentOutOfRangeException(nameof(broadcast), broadcast, null);
        }
    }

    /// <summary>
    /// The packet the per-recipient encoding sent <paramref name="recipient" /> on the tick just run, or null for none:
    /// every entity its tracking reported updated, described as the instance describes it for this recipient (a
    /// character's motion left out for itself, a character left with nothing dropped, a creature's routine selection
    /// plus its death and maximum health when marked), the list encoded whole.
    /// </summary>
    private static byte[]? PerRecipientEncoding(CharacterEntity recipient, List<MapInstanceClient> characters,
        List<Creature> creatures, PacketEncoder encoder)
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
