// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.Common;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;

namespace Avalon.World.Public.Characters;

public interface ICharacterGameState
{
    IReadOnlyList<ObjectGuid> NewObjects { get; }
    IReadOnlyList<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects { get; }
    IReadOnlyList<ObjectGuid> RemovedObjects { get; }

    /// <summary>
    /// Diffs this tick's objects against what the client already sees. Only the objects in its view
    /// count (#593): the watcher's own character always, every other one by <paramref name="range" />
    /// from <paramref name="watcherPosition" />, on X/Z.
    /// </summary>
    void Update(
        ObjectGuid watcher,
        Vector3 watcherPosition,
        InterestRange range,
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields);
}
