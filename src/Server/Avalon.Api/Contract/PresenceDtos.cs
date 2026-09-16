// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Api.Contract;

public class CharacterPresenceDto
{
    public uint CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Orientation { get; set; }
    public ushort Level { get; set; }
    public uint CurrentHealth { get; set; }
    public uint Health { get; set; }
    public string MoveState { get; set; } = string.Empty;
    public bool InCombat { get; set; }
    public bool Dead { get; set; }
    public DateTime LastSeen { get; set; }
}

public class InstancePresenceDto
{
    public Guid InstanceId { get; set; }
    public ushort TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public int Seed { get; set; }
    public PresenceMapType MapType { get; set; }
    public ushort WorldId { get; set; }
    public uint? OwnerCharacterId { get; set; }
    public IList<CharacterPresenceDto> Characters { get; set; } = [];
}

public class PlayerPresenceDto
{
    public CharacterPresenceDto Target { get; set; } = new();
    public InstancePresenceDto Instance { get; set; } = new();

    /// <summary>
    /// True when the config + chunk pool behind this instance's seed have changed since
    /// the instance was created, so a regenerated layout may not match the player's client.
    /// </summary>
    public bool LayoutStale { get; set; }

    /// <summary>When the world server captured this snapshot. The SPA shows its age.</summary>
    public DateTime CapturedAt { get; set; }
}
