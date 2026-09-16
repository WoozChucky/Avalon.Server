// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Api.Contract;

/// <summary>
/// One row of the online roster. Deliberately thin — the roster is a lookup index,
/// not a detail view. Carrying positions for every online player would defeat the
/// payload cap that server-side paging exists to provide.
/// </summary>
public class OnlinePlayerDto
{
    public uint CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public ushort Level { get; set; }
    public ushort WorldId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public ushort TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Map type. Null if the presence snapshot carried a map type this build does not recognise.</summary>
    public MapType? MapType { get; set; }

    public Guid InstanceId { get; set; }
    public DateTime LastSeen { get; set; }
}
