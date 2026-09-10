using System;
using System.IO;
using System.Linq;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Serialization;
using ProtoBuf;
using Xunit;

namespace Avalon.Server.World.UnitTests.Serialization;

/// <summary>
/// Holds entity replication to carrying the same information it has always carried.
/// </summary>
/// <remarks>
/// The expectations these compare against are the ones the previous format was held to, for
/// the same entities under the same field selections. Nothing here compares bytes with what
/// that format produced, which would be the wrong claim: the bytes are meant to be different
/// and no client will ever read the old ones again. What must not have changed is which
/// values a client ends up with, for every entity kind and every selection.
///
/// Each case goes through the serializer rather than stopping at the object, so what is
/// checked is what survives the wire.
/// </remarks>
public class ObjectStateWriterShould
{
    [Theory]
    [MemberData(nameof(EntityStateScenarios.Names), MemberType = typeof(EntityStateScenarios))]
    public void Carry_every_field_a_client_is_sent(string name)
    {
        EntityStateScenario scenario = EntityStateScenarios.Get(name);

        EntitySnapshot arrived = Project(scenario.Type, RoundTrip(Describe(scenario)));

        Assert.Equal(scenario.Expected, arrived);
    }

    [Theory]
    [MemberData(nameof(EntityStateScenarios.Names), MemberType = typeof(EntityStateScenarios))]
    public void Identify_the_entity_it_describes(string name)
    {
        EntityStateScenario scenario = EntityStateScenarios.Get(name);

        Assert.Equal(scenario.Guid.RawValue, RoundTrip(Describe(scenario)).Guid);
    }

    /// <summary>
    /// The payload no longer has to be told what it is before it can be read. Every kind of
    /// entity, added or updated, parses as the same message; the reader then asks which
    /// members arrived.
    /// </summary>
    /// <remarks>
    /// The previous format could not do this. Its shape was decided by the type byte of the
    /// guid together with whether the payload was an add or an update, and two of its five
    /// layouts began with a coordinate where the other three began with a bitmask, so a
    /// reader given the wrong pair produced values rather than an error.
    /// </remarks>
    [Fact]
    public void Parse_without_being_told_which_kind_of_entity_it_is()
    {
        foreach (EntityStateScenario scenario in EntityStateScenarios.All)
        {
            ObjectState parsed = RoundTrip(Describe(scenario));

            Assert.Equal(scenario.Guid.RawValue, parsed.Guid);
            Assert.Equal(scenario.Expected.Name, parsed.Name);
            Assert.Equal(scenario.Expected.PortalRole, parsed.PortalRole);
        }
    }

    /// <summary>
    /// A unit with no power type reports the type and neither amount, which is what the
    /// previous format did by skipping past them and what this does by leaving them out.
    /// </summary>
    [Fact]
    public void Send_no_power_amounts_for_a_unit_with_no_power_type()
    {
        EntityStateScenario scenario = EntityStateScenarios.Get("character-update-without-a-power-type");

        Assert.True(scenario.Fields.HasFlag(GameEntityFields.Power));
        Assert.True(scenario.Fields.HasFlag(GameEntityFields.CurrentPower));

        ObjectState state = RoundTrip(Describe(scenario));

        Assert.Equal(PowerType.None, state.PowerType);
        Assert.Null(state.Power);
        Assert.Null(state.CurrentPower);
    }

    /// <summary>
    /// Suppressed placement is absent, not zeroed. A client that read the difference wrongly
    /// would teleport the player to the origin ten times a second.
    /// </summary>
    [Fact]
    public void Leave_out_the_placement_of_a_character_sent_to_its_own_player()
    {
        EntityStateScenario scenario = EntityStateScenarios.Get("character-update-seen-by-its-own-player");

        ObjectState state = RoundTrip(Describe(scenario));

        Assert.Null(state.Position);
        Assert.Null(state.Velocity);
        Assert.Null(state.Orientation);

        // The rest of the character still travels; only placement is withheld.
        Assert.NotNull(state.CurrentHealth);
    }

    /// <summary>
    /// A member the entity has nothing to say about costs nothing. A projectile sets three of
    /// the nineteen members and pays for three.
    /// </summary>
    [Fact]
    public void Cost_nothing_for_the_members_an_entity_does_not_set()
    {
        ObjectState projectile = RoundTrip(Describe(EntityStateScenarios.Get("projectile-add")));

        Assert.All(
            new object?[]
            {
                projectile.MoveState, projectile.Health, projectile.CurrentHealth, projectile.PowerType,
                projectile.Power, projectile.CurrentPower, projectile.Level, projectile.IsDead,
                projectile.Experience, projectile.RequiredExperience, projectile.CreatureMetadataId,
                projectile.Name, projectile.PortalRadius, projectile.PortalTargetMapId, projectile.PortalRole,
            },
            Assert.Null);
    }

    private static ObjectState RoundTrip(ObjectState state)
    {
        using var stream = new MemoryStream();

        Serializer.Serialize(stream, state);
        stream.Position = 0;

        return Serializer.Deserialize<ObjectState>(stream);
    }

    /// <summary>
    /// Dispatches on the static type of the entity, the way the broadcast path does. Which
    /// overload applies is a compile-time decision here and there.
    /// </summary>
    private static ObjectState Describe(EntityStateScenario scenario) => scenario.Type switch
    {
        ObjectType.Character => ObjectStateWriter.From((ICharacter)scenario.Entity, scenario.Fields),
        ObjectType.Creature => ObjectStateWriter.From((ICreature)scenario.Entity, scenario.Fields),
        ObjectType.SpellProjectile when scenario.IsAdd => ObjectStateWriter.From((IWorldObject)scenario.Entity),
        ObjectType.SpellProjectile => ObjectStateWriter.From((IWorldObject)scenario.Entity, scenario.Fields),
        ObjectType.Portal => ObjectStateWriter.From((PortalInstance)scenario.Entity),
        _ => throw new InvalidOperationException($"No entity state is defined for {scenario.Type}."),
    };

    /// <summary>
    /// What a client knows after reading the message, in the same terms the previous format
    /// was measured in, so the two are comparable.
    /// </summary>
    private static EntitySnapshot Project(ObjectType type, ObjectState state) => new()
    {
        Type = type,
        Position = Vector(state.Position),
        Velocity = Vector(state.Velocity),
        Orientation = state.Orientation,
        MoveState = state.MoveState,
        Health = state.Health,
        CurrentHealth = state.CurrentHealth,
        PowerType = state.PowerType,
        Power = state.Power,
        CurrentPower = state.CurrentPower,
        Level = state.Level,
        IsDead = state.IsDead,
        Experience = state.Experience,
        RequiredExperience = state.RequiredExperience,
        CreatureMetadataId = state.CreatureMetadataId,
        Name = state.Name,
        PortalRadius = state.PortalRadius,
        PortalTargetMapId = state.PortalTargetMapId,
        PortalRole = state.PortalRole,
    };

    private static Vector3? Vector(Vec3? vector) =>
        vector is null ? null : new Vector3(vector.X, vector.Y, vector.Z);
}
