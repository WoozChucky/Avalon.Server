using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;

namespace Avalon.World.Characters;

/// <summary>The power pool each class casts from, set on the character at select.</summary>
public static class ClassPowerType
{
    public static PowerType Of(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Warrior => PowerType.Fury,
        CharacterClass.Wizard or CharacterClass.Healer => PowerType.Mana,
        CharacterClass.Hunter => PowerType.Energy,
        _ => PowerType.None
    };
}
