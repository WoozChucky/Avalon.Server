using System.IO;
using System.Text;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;

namespace Avalon.Server.World.UnitTests.Serialization;

/// <summary>
/// Reads the hand-rolled entity-field payload back into an <see cref="EntitySnapshot"/>.
/// </summary>
/// <remarks>
/// Nothing on the server has ever read this format; the only reader that existed lived in a
/// client. This is that reader, so the bytes the server writes can be held to carrying the
/// values they were built from.
///
/// The format: a four-byte little-endian bitmask, then the fields whose bits it sets, in a
/// fixed order and at fixed widths, then a trailer that depends on the entity type. Two of the
/// five layouts write no bitmask at all, and which layout applies is decided by the type byte
/// of the enclosing guid together with whether the payload is an add or an update - so a reader
/// must know both before it can tell whether byte zero begins a bitmask or a float.
/// </remarks>
internal static class EntityFieldDecoder
{
    internal static EntitySnapshot DecodeAdd(ObjectType type, byte[] bytes)
    {
        using BinaryReader reader = Reader(bytes);
        var snapshot = new Draft { Type = type };

        switch (type)
        {
            case ObjectType.Character:
                GameEntityFields fields = DecodeUnit(snapshot, reader);
                DecodeCharacterTail(snapshot, reader, fields);
                break;

            case ObjectType.Creature:
                DecodeUnit(snapshot, reader);
                DecodeCreatureTail(snapshot, reader);
                break;

            case ObjectType.SpellProjectile:
                // No bitmask: position, velocity and yaw, always all three.
                snapshot.Position = ReadVector(reader);
                snapshot.Velocity = ReadVector(reader);
                snapshot.Orientation = reader.ReadSingle();
                break;

            case ObjectType.Portal:
                // No bitmask either, and a different set of fields entirely.
                snapshot.Position = ReadVector(reader);
                snapshot.PortalRadius = reader.ReadSingle();

                // Two bytes, not four. The map identifier is a sixteen-bit value and the
                // writer emits it at its own width; a reader expecting four takes the role
                // byte as part of it and then runs off the end of the payload.
                snapshot.PortalTargetMapId = reader.ReadUInt16();
                snapshot.PortalRole = reader.ReadByte();
                break;

            default:
                throw new InvalidDataException($"No add layout is defined for {type}.");
        }

        AssertFullyConsumed(reader, type, "add");
        return snapshot.Build();
    }

    internal static EntitySnapshot DecodeUpdate(ObjectType type, byte[] bytes)
    {
        using BinaryReader reader = Reader(bytes);
        var snapshot = new Draft { Type = type };

        switch (type)
        {
            case ObjectType.Character:
                GameEntityFields fields = DecodeUnit(snapshot, reader);
                DecodeCharacterTail(snapshot, reader, fields);
                break;

            case ObjectType.Creature:
                DecodeUnit(snapshot, reader);
                DecodeCreatureTail(snapshot, reader);
                break;

            case ObjectType.SpellProjectile:
                DecodeWorldObject(snapshot, reader);
                break;

            default:
                // Portals are never updated, so no update layout was ever written for one.
                throw new InvalidDataException($"No update layout is defined for {type}.");
        }

        AssertFullyConsumed(reader, type, "update");
        return snapshot.Build();
    }

    private static void DecodeWorldObject(Draft snapshot, BinaryReader reader)
    {
        var fields = (GameEntityFields)reader.ReadInt32();

        if (fields.HasFlag(GameEntityFields.Position)) snapshot.Position = ReadVector(reader);
        if (fields.HasFlag(GameEntityFields.Velocity)) snapshot.Velocity = ReadVector(reader);
        if (fields.HasFlag(GameEntityFields.Orientation)) snapshot.Orientation = reader.ReadSingle();
    }

    private static GameEntityFields DecodeUnit(Draft snapshot, BinaryReader reader)
    {
        var fields = (GameEntityFields)reader.ReadInt32();

        if (fields.HasFlag(GameEntityFields.Position)) snapshot.Position = ReadVector(reader);
        if (fields.HasFlag(GameEntityFields.Velocity)) snapshot.Velocity = ReadVector(reader);
        if (fields.HasFlag(GameEntityFields.Orientation)) snapshot.Orientation = reader.ReadSingle();
        if (fields.HasFlag(GameEntityFields.MoveState)) snapshot.MoveState = (MoveState)reader.ReadByte();

        if (fields.HasFlag(GameEntityFields.Health)) snapshot.Health = reader.ReadUInt32();
        if (fields.HasFlag(GameEntityFields.CurrentHealth)) snapshot.CurrentHealth = reader.ReadUInt32();

        if (fields.HasFlag(GameEntityFields.PowerType))
        {
            snapshot.PowerType = (PowerType)reader.ReadByte();

            // The one conditional that reads a value rather than a bit: with no power type
            // there is nothing to size, so both power fields are skipped even when their bits
            // are set. A reader trusting the bitmask alone desynchronises from here on.
            if (snapshot.PowerType != Avalon.World.Public.Enums.PowerType.None)
            {
                if (fields.HasFlag(GameEntityFields.Power)) snapshot.Power = reader.ReadUInt32();
                if (fields.HasFlag(GameEntityFields.CurrentPower)) snapshot.CurrentPower = reader.ReadUInt32();
            }
        }

        if (fields.HasFlag(GameEntityFields.Level)) snapshot.Level = reader.ReadUInt16();
        if (fields.HasFlag(GameEntityFields.IsDead)) snapshot.IsDead = reader.ReadByte() != 0;

        return fields;
    }

    private static void DecodeCharacterTail(Draft snapshot, BinaryReader reader, GameEntityFields fields)
    {
        if (fields.HasFlag(GameEntityFields.Experience)) snapshot.Experience = reader.ReadUInt64();
        if (fields.HasFlag(GameEntityFields.RequiredExperience)) snapshot.RequiredExperience = reader.ReadUInt64();

        // Unconditional, and its bit is never tested.
        snapshot.Name = reader.ReadString();
    }

    private static void DecodeCreatureTail(Draft snapshot, BinaryReader reader)
    {
        // Both unconditional, and neither bit is ever tested.
        //
        // Eight bytes, not four. The identifier is a sixty-four-bit value everywhere on the
        // server and the writer emits it whole; a reader expecting four keeps the low half,
        // then reads the top half as the length prefix of the name and desynchronises.
        snapshot.CreatureMetadataId = reader.ReadUInt64();
        snapshot.Name = reader.ReadString();
    }

    private static Vector3 ReadVector(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static BinaryReader Reader(byte[] bytes) =>
        new(new MemoryStream(bytes, writable: false), Encoding.UTF8);

    /// <summary>
    /// Refuses a payload the reader did not read to the end of.
    /// </summary>
    /// <remarks>
    /// A width the reader has wrong shows up as leftover bytes rather than as a wrong value,
    /// and leftover bytes are silent: every field before the divergence still decodes. On the
    /// broadcast path the payloads are concatenated into one buffer and sliced out of it, so a
    /// reader that stopped early would have been reading a neighbour.
    /// </remarks>
    private static void AssertFullyConsumed(BinaryReader reader, ObjectType type, string layout)
    {
        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;

        if (remaining != 0)
        {
            throw new InvalidDataException(
                $"The {type} {layout} payload has {remaining} byte(s) left over after decoding, "
                + "so the reader and the writer disagree about a field width.");
        }
    }

    /// <summary>A snapshot under construction, since the finished one is immutable.</summary>
    private sealed class Draft
    {
        public ObjectType Type { get; init; }
        public Vector3? Position { get; set; }
        public Vector3? Velocity { get; set; }
        public float? Orientation { get; set; }
        public MoveState? MoveState { get; set; }
        public uint? Health { get; set; }
        public uint? CurrentHealth { get; set; }
        public PowerType? PowerType { get; set; }
        public uint? Power { get; set; }
        public uint? CurrentPower { get; set; }
        public ushort? Level { get; set; }
        public bool? IsDead { get; set; }
        public ulong? Experience { get; set; }
        public ulong? RequiredExperience { get; set; }
        public ulong? CreatureMetadataId { get; set; }
        public string? Name { get; set; }
        public float? PortalRadius { get; set; }
        public ushort? PortalTargetMapId { get; set; }
        public byte? PortalRole { get; set; }

        public EntitySnapshot Build() => new()
        {
            Type = Type,
            Position = Position,
            Velocity = Velocity,
            Orientation = Orientation,
            MoveState = MoveState,
            Health = Health,
            CurrentHealth = CurrentHealth,
            PowerType = PowerType,
            Power = Power,
            CurrentPower = CurrentPower,
            Level = Level,
            IsDead = IsDead,
            Experience = Experience,
            RequiredExperience = RequiredExperience,
            CreatureMetadataId = CreatureMetadataId,
            Name = Name,
            PortalRadius = PortalRadius,
            PortalTargetMapId = PortalTargetMapId,
            PortalRole = PortalRole,
        };
    }
}
