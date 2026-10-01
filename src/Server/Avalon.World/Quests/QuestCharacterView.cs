using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;

namespace Avalon.World.Quests;

/// <summary>A character as a quest script sees it (#433): four read-only values, no setter anywhere.</summary>
internal sealed class QuestCharacterView(CharacterEntity character) : IQuestCharacter
{
    public uint CharacterId => character.Guid.Id;
    public string Name => character.Name;
    public ushort Level => character.Level;
    public CharacterClass Class => character.Class;
}
