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

    /// <summary>
    /// The copy constructor is the one copy list (#1181 Part D): a shaped copy starts from it, and so does
    /// <see cref="AbilityMetadata.Clone" />. Every member set to a value of its own, none a default, so a member added
    /// later and left out of the list fails here.
    /// </summary>
    [Fact]
    public void Copy_every_member_through_the_copy_constructor()
    {
        PropertyInfo[] properties = typeof(AbilityMetadata).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var source = new AbilityMetadata();
        for (int i = 0; i < properties.Length; i++)
            properties[i].SetValue(source, Distinct(properties[i], i + 1));

        foreach (AbilityMetadata copy in (AbilityMetadata[])[new AbilityMetadata(source), source.Clone()])
        {
            Assert.NotSame(source, copy);
            Assert.All(properties, p => Assert.Equal(p.GetValue(source), p.GetValue(copy)));
        }
    }

    /// <summary>
    /// A value no default holds: a number its own (<paramref name="n" />, distinct per member), an enum its last value,
    /// a string the member's name, true, and a nullable one of them non-null.
    /// </summary>
    private static object Distinct(PropertyInfo property, int n)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(string))
            return property.Name;
        if (type == typeof(bool))
            return true;
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(Enum.GetValues(type).Length - 1)!;

        return Convert.ChangeType(100 + n, type);
    }

    /// <summary>
    /// A number, a flag, an enum or a string, or a nullable one of them: a value nothing can change through a reference
    /// to it.
    /// </summary>
    private static bool Unchanging(Type type) => Nullable.GetUnderlyingType(type) is { } inner
        ? Unchanging(inner)
        : type.IsPrimitive || type.IsEnum || type == typeof(string);
}
