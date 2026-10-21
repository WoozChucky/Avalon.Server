using Avalon.Common;
using Avalon.World.Combat;

namespace Avalon.Server.World.UnitTests.Combat;

public class ClassThreatModifierShould
{
    [Theory]
    [InlineData(CharacterClass.Warrior, 2.0f)]
    [InlineData(CharacterClass.Wizard, 1.1f)]
    [InlineData(CharacterClass.Hunter, 1.0f)]
    [InlineData(CharacterClass.Cleric, 1.0f)]
    public void Should_return_class_multiplier(CharacterClass cls, float expected)
    {
        Assert.Equal(expected, ClassThreatModifier.Get(cls));
    }
}
