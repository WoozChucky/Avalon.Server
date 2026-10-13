using System.Reflection;
using System.Runtime.CompilerServices;
using Avalon.World.Public.Abilities;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// One <see cref="AbilityMetadata" /> is shared by a caster's ability and the copy every one of its casts hands its
/// script (<c>GameAbility.CloneForCast</c>, #925). That is only safe while metadata cannot change once built: a member
/// a script could write, or a collection it could fill, would change the ability for its caster and for every cast
/// in flight.
/// </summary>
public class AbilityMetadataShould
{
    [Fact]
    public void Hold_nothing_that_can_change_once_it_is_built()
    {
        Type metadata = typeof(AbilityMetadata);
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Sealed, and straight under object: what is checked below is then everything an instance can hold.
        Assert.True(metadata.IsSealed, "A subclass could add a member that changes.");
        Assert.Equal(typeof(object), metadata.BaseType);

        List<string> changeable = [];
        foreach (FieldInfo field in metadata.GetFields(Instance))
        {
            // The field behind a get-and-init property is init-only; the one behind a settable property, or a
            // captured constructor parameter, is not.
            if (!field.IsInitOnly)
                changeable.Add($"{field.Name} can be written after construction");
            else if (!Unchanging(field.FieldType))
                changeable.Add($"{field.Name}: {field.FieldType.Name} can be changed in place");
        }

        foreach (PropertyInfo property in metadata.GetProperties(Instance))
        {
            if (property.SetMethod is { } setter && !setter.ReturnParameter.GetRequiredCustomModifiers()
                    .Contains(typeof(IsExternalInit)))
            {
                changeable.Add($"{property.Name} has a setter");
            }
        }

        Assert.Empty(changeable);
        Assert.NotEmpty(metadata.GetFields(Instance));
    }

    /// <summary>A number, a flag, an enum or a string: a value nothing can change through a reference to it.</summary>
    private static bool Unchanging(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string);
}
