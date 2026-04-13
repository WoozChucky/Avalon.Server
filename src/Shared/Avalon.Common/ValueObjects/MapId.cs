// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

namespace Avalon.Common.ValueObjects;

public class MapId(ushort value) : ValueObject<ushort>(value), IHideObjectMembers
{
    public static implicit operator ushort(MapId mapId) => mapId.Value;
    public static implicit operator MapId(ushort value) => new(value);
    public static implicit operator MapId(MapTemplateId value) => new(value);
}
