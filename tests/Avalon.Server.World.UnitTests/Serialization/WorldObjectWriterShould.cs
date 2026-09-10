using System;
using Avalon.Common;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Serialization;
using Xunit;

namespace Avalon.Server.World.UnitTests.Serialization;

/// <summary>
/// Holds the entity-state payload to carrying the values it was built from, for every layout
/// the broadcast path can produce.
/// </summary>
/// <remarks>
/// The format had no test of any kind and no reader on this side, so nothing could say what
/// it carried: the encoder agreed with itself by construction, and the only reader that ever
/// existed lived somewhere else. These read the bytes back and compare against what a client
/// is entitled to end up knowing, which is the claim worth making and the one that survives a
/// change of format.
/// </remarks>
public class WorldObjectWriterShould
{
    [Theory]
    [MemberData(nameof(EntityStateScenarios.Names), MemberType = typeof(EntityStateScenarios))]
    public void Carry_every_field_a_client_is_sent(string name)
    {
        EntityStateScenario scenario = EntityStateScenarios.Get(name);

        EntitySnapshot decoded = Decode(scenario);

        Assert.Equal(scenario.Expected, decoded);
    }

    /// <summary>
    /// A set bit does not mean a field is present. The power type is read as a value, and
    /// when it says there is none the two power fields are skipped whatever their bits say.
    /// </summary>
    [Fact]
    public void Skip_both_power_fields_when_the_unit_has_no_power_type()
    {
        EntityStateScenario scenario = EntityStateScenarios.Get("character-update-without-a-power-type");

        Assert.True(scenario.Fields.HasFlag(GameEntityFields.Power));
        Assert.True(scenario.Fields.HasFlag(GameEntityFields.CurrentPower));

        EntitySnapshot decoded = Decode(scenario);

        Assert.Null(decoded.Power);
        Assert.Null(decoded.CurrentPower);
    }

    /// <summary>
    /// Two of the five layouts write no bitmask, so the first four bytes of the payload are
    /// part of a coordinate. A reader that assumed a header everywhere would take them for
    /// one and mis-read the rest.
    /// </summary>
    [Theory]
    [InlineData("projectile-add")]
    [InlineData("portal-add")]
    public void Write_no_bitmask_at_all_for_two_of_the_layouts(string name)
    {
        EntityStateScenario scenario = EntityStateScenarios.Get(name);

        byte[] payload = Encode(scenario);
        int asIfItWereAHeader = BitConverter.ToInt32(payload, 0);

        Assert.NotEqual((int)scenario.Fields, asIfItWereAHeader);
    }

    /// <summary>
    /// Two of the sixteen bits are decorative: the writer tests neither and sends both values
    /// whatever is asked for.
    /// </summary>
    [Fact]
    public void Send_the_creature_identifier_and_name_even_when_their_bits_are_clear()
    {
        EntityStateScenario scenario = EntityStateScenarios.Get("creature-update");

        Assert.False(scenario.Fields.HasFlag(GameEntityFields.CreatureMetadataId));
        Assert.False(scenario.Fields.HasFlag(GameEntityFields.Name));

        EntitySnapshot decoded = Decode(scenario);

        Assert.NotNull(decoded.CreatureMetadataId);
        Assert.NotNull(decoded.Name);
    }

    private static EntitySnapshot Decode(EntityStateScenario scenario)
    {
        byte[] payload = Encode(scenario);

        return scenario.IsAdd
            ? EntityFieldDecoder.DecodeAdd(scenario.Type, payload)
            : EntityFieldDecoder.DecodeUpdate(scenario.Type, payload);
    }

    /// <summary>
    /// Dispatches the way the broadcast path does: on the type byte of the guid, and on
    /// whether this is an add or an update. Neither is in the payload.
    /// </summary>
    private static byte[] Encode(EntityStateScenario scenario)
    {
        byte[] buffer = new byte[4096];
        using var writer = new WorldObjectWriter(buffer);

        switch (scenario.Type)
        {
            case ObjectType.Character:
                writer.Write((ICharacter)scenario.Entity, scenario.Fields);
                break;

            case ObjectType.Creature:
                writer.Write((ICreature)scenario.Entity, scenario.Fields);
                break;

            case ObjectType.SpellProjectile when scenario.IsAdd:
                writer.Write((IWorldObject)scenario.Entity);
                break;

            case ObjectType.SpellProjectile:
                writer.Write((IWorldObject)scenario.Entity, scenario.Fields);
                break;

            case ObjectType.Portal:
                writer.Write((PortalInstance)scenario.Entity);
                break;

            default:
                throw new InvalidOperationException($"No layout is defined for {scenario.Type}.");
        }

        int length = (int)writer.BaseStream.Position;
        byte[] payload = new byte[length];
        Array.Copy(buffer, payload, length);

        return payload;
    }
}
