using Avalon.World.Public.Enums;

namespace Avalon.World.Combat;

public static class ClassThreatModifier
{
    public static float Get(CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior => 2.0f,
        CharacterClass.Wizard  => 1.1f,
        CharacterClass.Hunter  => 1.0f,
        CharacterClass.Healer  => 1.0f,
        _                      => 1.0f,
    };
}
