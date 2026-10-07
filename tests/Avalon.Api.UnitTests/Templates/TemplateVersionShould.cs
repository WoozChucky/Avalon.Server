using Avalon.Api.Templates;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Avalon.Api.UnitTests.Templates;

/// <summary>
/// A template's version covers every stored column, so a save made from a stale read is refused whichever
/// column changed meanwhile. The columns come from the EF model, so a column added later is tested with no edit here.
/// </summary>
public class TemplateVersionShould
{
    private static readonly string[] NotStored = ["Stackable", "BodyRemoveTimer"];

    public static TheoryData<string, string> ItemColumns => Columns(typeof(ItemTemplate));
    public static TheoryData<string, string> AbilityColumns => Columns(typeof(AbilityTemplate));
    public static TheoryData<string, string> CreatureColumns => Columns(typeof(CreatureTemplate));
    public static TheoryData<string, string> AuraColumns => Columns(typeof(AuraTemplate));

    [Fact]
    public void Give_the_same_version_for_the_same_row()
    {
        Assert.Equal(TemplateVersion.Of(Item()), TemplateVersion.Of(Item()));
        Assert.Equal(TemplateVersion.Of(Ability()), TemplateVersion.Of(Ability()));
        Assert.Equal(TemplateVersion.Of(Creature()), TemplateVersion.Of(Creature()));
    }

    [Fact]
    public void Give_a_lowercase_hex_sha256()
    {
        string version = TemplateVersion.Of(Item());

        Assert.Matches("^[0-9a-f]{64}$", version);
    }

    [Fact]
    public void Leave_out_the_computed_and_unmapped_properties()
    {
        Assert.DoesNotContain("Stackable", TemplateVersion.ColumnsOf(typeof(ItemTemplate)));
        Assert.DoesNotContain("BodyRemoveTimer", TemplateVersion.ColumnsOf(typeof(CreatureTemplate)));
    }

    [Fact]
    public void Tell_apart_the_same_classes_in_another_order()
    {
        ItemTemplate a = Item();
        a.AllowedClasses = [CharacterClass.Warrior, CharacterClass.Wizard];
        ItemTemplate b = Item();
        b.AllowedClasses = [CharacterClass.Wizard, CharacterClass.Warrior];

        Assert.NotEqual(TemplateVersion.Of(a), TemplateVersion.Of(b));
    }

    [Theory]
    [MemberData(nameof(ItemColumns))]
    public void Change_with_any_item_column(string kind, string column) =>
        AssertChanges(Item, TemplateVersion.Of, column);

    [Theory]
    [MemberData(nameof(AbilityColumns))]
    public void Change_with_any_ability_column(string kind, string column) =>
        AssertChanges(Ability, TemplateVersion.Of, column);

    [Theory]
    [MemberData(nameof(CreatureColumns))]
    public void Change_with_any_creature_column(string kind, string column) =>
        AssertChanges(Creature, TemplateVersion.Of, column);

    [Theory]
    [MemberData(nameof(AuraColumns))]
    public void Change_with_any_aura_column(string kind, string column) =>
        AssertChanges(Aura, TemplateVersion.Of, column);

    [Fact]
    public void Cover_the_base_damage_coefficient_of_an_aura() =>
        Assert.Contains(nameof(AuraTemplate.BaseDamageCoefficient), TemplateVersion.ColumnsOf(typeof(AuraTemplate)));

    [Fact]
    public void Change_with_any_aura_modifier_and_not_with_their_order()
    {
        string version = TemplateVersion.Of(Aura());

        AuraTemplate value = Aura();
        value.Modifiers[0].Value += 1f;
        AuraTemplate kind = Aura();
        kind.Modifiers[0].Kind = AuraModifierKind.Flat;
        AuraTemplate added = Aura();
        added.Modifiers.Add(new AuraStatModifier { AuraId = added.Id, Stat = AuraStat.MaxHealth, Kind = AuraModifierKind.Flat, Value = 5f });
        AuraTemplate removed = Aura();
        removed.Modifiers.RemoveAt(0);
        AuraTemplate reordered = Aura();
        reordered.Modifiers.Reverse();

        Assert.Equal(5, new[] { version, TemplateVersion.Of(value), TemplateVersion.Of(kind), TemplateVersion.Of(added), TemplateVersion.Of(removed) }
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(version, TemplateVersion.Of(reordered));
    }

    private static void AssertChanges<T>(Func<T> make, Func<T, string> version, string column) where T : class
    {
        T original = make();
        T changed = make();
        System.Reflection.PropertyInfo property = typeof(T).GetProperty(column)!;
        property.SetValue(changed, Different(property.PropertyType, property.GetValue(changed)));

        Assert.NotEqual(version(original), version(changed));
    }

    /// <summary>A value of the type that is not <paramref name="current"/>.</summary>
    private static object? Different(Type type, object? current)
    {
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return current is null ? Different(nullable, null) : null;

        if (type == typeof(string)) return current + "x";
        if (type == typeof(bool)) return !(bool)current!;
        if (type.IsEnum)
        {
            Array values = Enum.GetValues(type);
            foreach (object value in values)
                if (!value.Equals(current)) return value;
            throw new InvalidOperationException($"{type.Name} has one value.");
        }

        if (type == typeof(List<CharacterClass>))
        {
            var list = new List<CharacterClass>((List<CharacterClass>)current!);
            if (list.Count > 0) list.RemoveAt(list.Count - 1); else list.Add(CharacterClass.Warrior);
            return list;
        }

        Type? valueObject = ValueObjectType(type);
        if (valueObject is not null)
        {
            dynamic raw = type.GetProperty("Value")!.GetValue(current)!;
            return Activator.CreateInstance(type, checked(raw + 1));
        }

        if (current is null)
            return Convert.ChangeType(1, type, System.Globalization.CultureInfo.InvariantCulture);

        if (current is IConvertible)
        {
            return Convert.ChangeType(Convert.ToDouble(current, System.Globalization.CultureInfo.InvariantCulture) + 1, type,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        throw new NotSupportedException($"No way to vary a {type.Name}; teach {nameof(Different)} about it.");
    }

    private static Type? ValueObjectType(Type type)
    {
        for (Type? t = type; t is not null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ValueObject<>)) return t;
        return null;
    }

    private static TheoryData<string, string> Columns(Type entity)
    {
        // Straight from the EF model, built here separately from the one the version uses.
        using WorldDbContext context = new(new DbContextOptionsBuilder<WorldDbContext>()
            .UseSqlite("DataSource=:memory:").Options);
        IEntityType type = context.Model.FindEntityType(entity)!;
        TheoryData<string, string> data = [];
        foreach (IProperty property in type.GetProperties().Where(p => !p.IsShadowProperty()))
            data.Add(entity.Name, property.Name);
        Assert.DoesNotContain(type.GetProperties(), p => NotStored.Contains(p.Name, StringComparer.Ordinal));
        return data;
    }

    private static ItemTemplate Item() => new()
    {
        Id = new ItemTemplateId(5),
        Name = "Sword",
        MaxStackSize = 1,
        AllowedClasses = [CharacterClass.Warrior, CharacterClass.Hunter],
        StatType1 = StatType.Strength,
        StatValue1 = 3,
        DamageMin1 = 2,
        DamageMax1 = 4,
        DamageType1 = DamageType.Physical,
    };

    private static AbilityTemplate Ability() => new()
    {
        Id = new AbilityId(7),
        Name = "Cleave",
        ScriptName = "script",
        AllowedClasses = [CharacterClass.Warrior],
        AuraId = new AuraId(1),
    };

    private static AuraTemplate Aura() => new()
    {
        Id = new AuraId(3),
        Name = "Ward",
        Icon = "ward",
        Kind = AuraKind.Helpful,
        DurationMs = 10000,
        Stacking = AuraStacking.Refresh,
        MaxStacks = 1,
        Modifiers =
        [
            new AuraStatModifier { AuraId = new AuraId(3), Stat = AuraStat.Armor, Kind = AuraModifierKind.Percent, Value = 10f },
            new AuraStatModifier { AuraId = new AuraId(3), Stat = AuraStat.DodgePct, Kind = AuraModifierKind.Flat, Value = 2f },
        ],
    };

    private static CreatureTemplate Creature() => new()
    {
        Id = new CreatureTemplateId(9),
        Name = "Wolf",
        LootTableId = new LootTableId(2),
    };
}
