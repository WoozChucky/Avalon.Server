using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;

namespace Avalon.World.Public.Scripts;

/// <summary>
/// A creature as a quest script sees it (#433): a snapshot taken when the hook is called, with nothing that
/// changes the creature. <see cref="Guid" /> is a copy, so changing it changes nothing live.
/// </summary>
public sealed record QuestCreatureView(ObjectGuid Guid, CreatureTemplateId TemplateId, string Name, ushort Level, bool IsDead)
{
    public static QuestCreatureView From(ICreature creature) => new(
        new ObjectGuid(creature.Guid.RawValue), creature.Metadata.Id, creature.Name, creature.Level,
        creature.CurrentHealth == 0);
}

/// <summary>An instance as a quest script sees it (#433): which one and of what map, and nothing that changes it.</summary>
public sealed record QuestInstanceView(Guid InstanceId, MapTemplateId MapTemplateId, MapType MapType)
{
    public static QuestInstanceView From(IMapInstance instance) =>
        new(instance.InstanceId, instance.TemplateId, instance.MapType);
}
