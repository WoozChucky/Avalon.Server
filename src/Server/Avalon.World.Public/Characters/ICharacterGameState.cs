// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Common;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Characters;

public interface ICharacterGameState
{
    ISet<ObjectGuid> NewObjects { get; }
    ISet<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects { get; }
    ISet<ObjectGuid> RemovedObjects { get; }

    void Update(
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> chunkObjects);
}
