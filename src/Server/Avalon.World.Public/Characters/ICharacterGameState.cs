// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.Common;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Characters;

public interface ICharacterGameState
{
    IReadOnlyList<ObjectGuid> NewObjects { get; }
    IReadOnlyList<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects { get; }
    IReadOnlyList<ObjectGuid> RemovedObjects { get; }

    void Update(
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields);
}
