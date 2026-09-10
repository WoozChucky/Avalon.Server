using System.Collections.Generic;
using System.Linq;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.Server.World.UnitTests.Serialization;

/// <summary>
/// One entity, the field selection the broadcast path would send it under, and the
/// information a client is entitled to end up with.
/// </summary>
/// <remarks>
/// <see cref="Expected"/> is written out rather than read back off the entity, because the two
/// are not the same thing and the difference is the point: a broadcast to the player who owns
/// the character suppresses its position, a creature carries no experience however much the
/// field selection asks for, and a unit with no power type sends neither power value even
/// though both bits are set. Deriving the expectation from the entity would assert that the
/// encoder agrees with itself.
/// </remarks>
internal sealed record EntityStateScenario
{
    public required string Name { get; init; }

    public required ObjectGuid Guid { get; init; }

    /// <summary>Adds and updates are separate layouts for the same entity, not one layout twice.</summary>
    public required bool IsAdd { get; init; }

    /// <summary>The entity itself. Its type decides which of the five layouts applies.</summary>
    public required object Entity { get; init; }

    /// <summary>
    /// What the broadcast path asks for. Ignored by the two layouts that write no bitmask,
    /// which send a fixed set of fields whatever is dirty.
    /// </summary>
    public GameEntityFields Fields { get; init; }

    public required EntitySnapshot Expected { get; init; }

    public ObjectType Type => Guid.Type;

    public override string ToString() => Name;
}

/// <summary>
/// Representative entities for all five layouts, plus the cases where the format does
/// something other than what its bitmask says.
/// </summary>
internal static class EntityStateScenarios
{
    private const string PlainName = "Aeliana";

    /// <summary>
    /// 132 UTF-8 bytes, so the length prefix needs a second byte and a reader that counts
    /// characters rather than bytes disagrees about where the name ends.
    /// </summary>
    private static readonly string LongName = string.Concat(Enumerable.Repeat("Ωx", 44));

    private static readonly Vector3 CharacterPosition = new(12.5f, -3.25f, 800.125f);
    private static readonly Vector3 CharacterVelocity = new(0.5f, 0f, -1.75f);
    private const float CharacterYaw = 2.75f;

    private static readonly Vector3 CreaturePosition = new(-40.5f, 0.25f, 16f);
    private static readonly Vector3 CreatureVelocity = new(1f, 0f, 0f);

    /// <summary>
    /// x and z are deliberately not zero. Only y travels, so a reader that took the whole
    /// vector would show it.
    /// </summary>
    private static readonly Vector3 CreatureOrientation = new(9.5f, -1.5f, -9.5f);

    private static readonly Vector3 ProjectilePosition = new(1f, 2f, 3f);
    private static readonly Vector3 ProjectileVelocity = new(0f, -9.81f, 0f);
    private static readonly Vector3 ProjectileOrientation = new(4.5f, 0.75f, -4.5f);

    private static readonly Vector3 PortalPosition = new(100f, 0f, -100f);

    /// <summary>
    /// Past what fits in 32 bits. The identifier is declared as a 64-bit value and travels as
    /// one, so a narrower reader loses the top half of it rather than failing.
    /// </summary>
    private const ulong CreatureTemplate = 900_719_925_474_099UL;

    private static readonly ObjectGuid CharacterGuid = new(ObjectType.Character, 4242);
    private static readonly ObjectGuid CreatureGuid = new(ObjectType.Creature, 77);
    private static readonly ObjectGuid ProjectileGuid = new(ObjectType.SpellProjectile, 9);
    private static readonly ObjectGuid PortalGuid = new(ObjectType.Portal, 3);

    internal static IReadOnlyList<EntityStateScenario> All { get; } = Build();

    /// <summary>The names, for a theory that takes one scenario at a time.</summary>
    public static TheoryData<string> Names
    {
        get
        {
            var names = new TheoryData<string>();

            foreach (EntityStateScenario scenario in All)
            {
                names.Add(scenario.Name);
            }

            return names;
        }
    }

    internal static EntityStateScenario Get(string name) =>
        All.Single(scenario => string.Equals(scenario.Name, name, System.StringComparison.Ordinal));

    private static List<EntityStateScenario> Build() =>
    [
        CharacterSeenByAnotherPlayer(),
        CharacterSeenByItsOwnPlayer(),
        CharacterUpdateSeenByAnotherPlayer(),
        CharacterUpdateSeenByItsOwnPlayer(),
        CharacterUpdateWithoutAPowerType(),
        DeadCharacterWithALongName(),
        CreatureAdd(),
        CreatureUpdate(),
        ProjectileAdd(),
        ProjectileUpdateWithEverythingDirty(),
        ProjectileUpdateWithOnlyPositionDirty(),
        PortalAdd(),
    ];

    private static ICharacter NewCharacter(
        PowerType powerType = PowerType.Mana,
        bool isDead = false,
        string name = PlainName) =>
        new CharacterEntity
        {
            Data = new Character
            {
                Id = 4242u,
                Name = name,
                Level = 42,
                Health = 4100,
                Power1 = 900,
                Experience = 12_345_678_901UL,
                X = CharacterPosition.x,
                Y = CharacterPosition.y,
                Z = CharacterPosition.z,
                Rotation = CharacterYaw,
            },
            Guid = CharacterGuid,
            Velocity = CharacterVelocity,
            MoveState = MoveState.Running,
            CurrentHealth = 3777u,
            PowerType = powerType,
            CurrentPower = 128u,
            RequiredExperience = 99_999_999_999UL,
            IsDead = isDead,
        };

    private static ICreature NewCreature() =>
        new Creature
        {
            Guid = CreatureGuid,
            Name = "Direwolf",
            Metadata = new CreatureTemplate { Id = CreatureTemplate },
            Position = CreaturePosition,
            Velocity = CreatureVelocity,
            Orientation = CreatureOrientation,
            MoveState = MoveState.Walking,
            Health = 250u,
            CurrentHealth = 175u,
            PowerType = PowerType.Fury,
            Power = 100u,
            CurrentPower = 40u,
            Level = 12,
        };

    private static IWorldObject NewProjectile() => new ProjectileStub
    {
        Guid = ProjectileGuid,
        Position = ProjectilePosition,
        Velocity = ProjectileVelocity,
        Orientation = ProjectileOrientation,
    };

    private static EntitySnapshot CharacterBase(bool isDead = false, string name = PlainName) => new()
    {
        Type = ObjectType.Character,
        Position = CharacterPosition,
        Velocity = CharacterVelocity,

        // The character stores its facing as a single angle, so the vector it reports has
        // zero for x and z and the yaw-only truncation is invisible here. The creature
        // scenarios are where it shows.
        Orientation = CharacterYaw,
        MoveState = MoveState.Running,
        Health = 4100u,
        CurrentHealth = 3777u,
        PowerType = PowerType.Mana,
        Power = 900u,
        CurrentPower = 128u,
        Level = 42,
        IsDead = isDead,
        Experience = 12_345_678_901UL,
        RequiredExperience = 99_999_999_999UL,
        Name = name,
    };

    private static EntityStateScenario CharacterSeenByAnotherPlayer() => new()
    {
        Name = "character-add-seen-by-another-player",
        Guid = CharacterGuid,
        IsAdd = true,
        Entity = NewCharacter(),
        Fields = MapInstance.MaskSelfSuppression(GameEntityFields.All, CharacterGuid, OtherPlayer),
        Expected = CharacterBase(),
    };

    /// <summary>
    /// A player's own character. Its position travels on a different packet, so the broadcast
    /// strips the three placement fields and sends the rest.
    /// </summary>
    private static EntityStateScenario CharacterSeenByItsOwnPlayer() => new()
    {
        Name = "character-add-seen-by-its-own-player",
        Guid = CharacterGuid,
        IsAdd = true,
        Entity = NewCharacter(),
        Fields = MapInstance.MaskSelfSuppression(GameEntityFields.All, CharacterGuid, CharacterGuid),
        Expected = CharacterBase() with { Position = null, Velocity = null, Orientation = null },
    };

    private static EntityStateScenario CharacterUpdateSeenByAnotherPlayer() => new()
    {
        Name = "character-update-seen-by-another-player",
        Guid = CharacterGuid,
        IsAdd = false,
        Entity = NewCharacter(),
        Fields = MapInstance.MaskSelfSuppression(GameEntityFields.CharacterUpdate, CharacterGuid, OtherPlayer),
        Expected = CharacterBase(),
    };

    private static EntityStateScenario CharacterUpdateSeenByItsOwnPlayer() => new()
    {
        Name = "character-update-seen-by-its-own-player",
        Guid = CharacterGuid,
        IsAdd = false,
        Entity = NewCharacter(),
        Fields = MapInstance.MaskSelfSuppression(GameEntityFields.CharacterUpdate, CharacterGuid, CharacterGuid),
        Expected = CharacterBase() with { Position = null, Velocity = null, Orientation = null },
    };

    /// <summary>
    /// The one conditional that reads a value instead of a bit. Both power fields are asked
    /// for and neither is sent, because there is no power type to size them against.
    /// </summary>
    private static EntityStateScenario CharacterUpdateWithoutAPowerType() => new()
    {
        Name = "character-update-without-a-power-type",
        Guid = CharacterGuid,
        IsAdd = false,
        Entity = NewCharacter(PowerType.None),
        Fields = MapInstance.MaskSelfSuppression(GameEntityFields.CharacterUpdate, CharacterGuid, OtherPlayer),
        Expected = CharacterBase() with
        {
            PowerType = PowerType.None,
            Power = null,
            CurrentPower = null,
        },
    };

    private static EntityStateScenario DeadCharacterWithALongName() => new()
    {
        Name = "character-add-dead-with-a-long-name",
        Guid = CharacterGuid,
        IsAdd = true,
        Entity = NewCharacter(isDead: true, name: LongName),
        Fields = MapInstance.MaskSelfSuppression(GameEntityFields.All, CharacterGuid, OtherPlayer),
        Expected = CharacterBase(isDead: true, name: LongName),
    };

    private static EntitySnapshot CreatureBase() => new()
    {
        Type = ObjectType.Creature,
        Position = CreaturePosition,
        Velocity = CreatureVelocity,
        Orientation = CreatureOrientation.y,
        MoveState = MoveState.Walking,
        PowerType = PowerType.Fury,
        CurrentPower = 40u,
        CreatureMetadataId = CreatureTemplate,
        Name = "Direwolf",
    };

    /// <summary>
    /// The whole field selection, which for a creature means every unit field plus the two
    /// the writer sends whatever is asked. Experience is not among them: it belongs to the
    /// character layout, so asking for it on a creature sends nothing.
    /// </summary>
    private static EntityStateScenario CreatureAdd() => new()
    {
        Name = "creature-add",
        Guid = CreatureGuid,
        IsAdd = true,
        Entity = NewCreature(),
        Fields = GameEntityFields.All,
        Expected = CreatureBase() with
        {
            Health = 250u,
            CurrentHealth = 175u,
            Power = 100u,
            Level = 12,

            // A creature has no such state. The field selection asks for it anyway and a
            // value goes out, because the selection is shared with the character layout.
            IsDead = false,
        },
    };

    private static EntityStateScenario CreatureUpdate() => new()
    {
        Name = "creature-update",
        Guid = CreatureGuid,
        IsAdd = false,
        Entity = NewCreature(),
        Fields = GameEntityFields.CreatureUpdate,
        Expected = CreatureBase() with { CurrentHealth = 175u },
    };

    /// <summary>The layout with no bitmask: three fields, always, whatever changed.</summary>
    private static EntityStateScenario ProjectileAdd() => new()
    {
        Name = "projectile-add",
        Guid = ProjectileGuid,
        IsAdd = true,
        Entity = NewProjectile(),
        Fields = GameEntityFields.All,
        Expected = new EntitySnapshot
        {
            Type = ObjectType.SpellProjectile,
            Position = ProjectilePosition,
            Velocity = ProjectileVelocity,
            Orientation = ProjectileOrientation.y,
        },
    };

    private static EntityStateScenario ProjectileUpdateWithEverythingDirty() => new()
    {
        Name = "projectile-update-with-everything-dirty",
        Guid = ProjectileGuid,
        IsAdd = false,
        Entity = NewProjectile(),
        Fields = GameEntityFields.WorldObjectUpdate,
        Expected = new EntitySnapshot
        {
            Type = ObjectType.SpellProjectile,
            Position = ProjectilePosition,
            Velocity = ProjectileVelocity,
            Orientation = ProjectileOrientation.y,
        },
    };

    /// <summary>
    /// The only path that carries a real dirty selection rather than a constant one, so it is
    /// the only place a partly-filled payload occurs today.
    /// </summary>
    private static EntityStateScenario ProjectileUpdateWithOnlyPositionDirty() => new()
    {
        Name = "projectile-update-with-only-position-dirty",
        Guid = ProjectileGuid,
        IsAdd = false,
        Entity = NewProjectile(),
        Fields = GameEntityFields.Position,
        Expected = new EntitySnapshot
        {
            Type = ObjectType.SpellProjectile,
            Position = ProjectilePosition,
        },
    };

    /// <summary>The other layout with no bitmask, and the only one with fields of its own.</summary>
    private static EntityStateScenario PortalAdd() => new()
    {
        Name = "portal-add",
        Guid = PortalGuid,
        IsAdd = true,
        Entity = new PortalInstance(PortalGuid, PortalPosition, radius: 2.5f, targetMapId: 1200, role: 1),
        Fields = GameEntityFields.All,
        Expected = new EntitySnapshot
        {
            Type = ObjectType.Portal,
            Position = PortalPosition,
            PortalRadius = 2.5f,
            PortalTargetMapId = 1200,
            PortalRole = 1,
        },
    };

    private static ObjectGuid OtherPlayer { get; } = new(ObjectType.Character, 5150);

    /// <summary>
    /// A projectile in flight. The live type is a script with a lifecycle attached; nothing
    /// but the three placement fields is read here, so the placement is all this supplies.
    /// </summary>
    private sealed class ProjectileStub : IWorldObject
    {
        public ObjectGuid Guid { get; set; } = new();
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector3 Orientation { get; set; }
    }
}
