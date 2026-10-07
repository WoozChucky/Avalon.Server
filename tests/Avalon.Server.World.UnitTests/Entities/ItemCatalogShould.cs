using System.Text.Json;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Exporter;
using Avalon.World.Public.Enums;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// The catalog is the only thing that turns the template id in an inventory packet into a name and
/// an icon. Rendering is pure and tested here; reading the rows is EF and is not.
///
/// The brief for this task defined a hand-written 13-field CatalogRow and tested that shape. This
/// widens the catalog to every field on ItemTemplate -- the same field set item-schema-v1.json
/// already documents -- so these tests were updated to match: every behaviour the brief checked
/// (empty catalog, null name, id ordering, readiness) still holds, and a round trip of a
/// representative damage/stat field is added, since that survival is the entire point of widening.
/// </summary>
public class ItemCatalogShould
{
    private static ItemTemplate Template(ulong id, string? name) => new()
    {
        Id = new ItemTemplateId(id),
        Name = name!,
        Class = ItemClass.Weapon,
        SubClass = ItemSubClass.OneHanded,
        Rarity = ItemRarity.Rare,
        DisplayId = 12,
        MaxStackSize = 1,
    };

    /// <summary>
    /// The column is nullable in practice, and a client parsing null into a label shows nothing
    /// where a name belongs. CharacterService.MapItem already coalesces the same way. This also
    /// pins the DefaultIgnoreCondition interaction: WhenWritingNull would otherwise drop the key
    /// entirely rather than write "" -- the property must still be present.
    /// </summary>
    [Fact]
    public void Render_A_Null_Name_As_An_Empty_String_Never_Omitted()
    {
        using var document = JsonDocument.Parse(ItemCatalog.Render([Template(1, null)]));

        JsonElement item = document.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.True(item.TryGetProperty("name", out JsonElement name));
        Assert.Equal(JsonValueKind.String, name.ValueKind);
        Assert.Equal(string.Empty, name.GetString());
    }

    [Fact]
    public void Render_Rows_In_Id_Order_So_A_Diff_Reads()
    {
        string json = ItemCatalog.Render([Template(30, "c"), Template(10, "a"), Template(20, "b")]);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            [10L, 20L, 30L],
            document.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetInt64()));
    }

    /// <summary>
    /// The whole point of widening past the brief's 13-field row: a tooltip needs the damage and
    /// stat fields, and the old CatalogRow dropped them entirely. This is a representative one of
    /// each, surviving the round trip untouched.
    /// </summary>
    [Fact]
    public void Render_A_Representative_Damage_And_Stat_Field()
    {
        ItemTemplate template = Template(1, "Rusted Sword");
        template.DamageMin1 = 4;
        template.DamageMax1 = 9;
        template.DamageType1 = DamageType.Physical;
        template.StatType1 = StatType.Strength;
        template.StatValue1 = 7;

        using var document = JsonDocument.Parse(ItemCatalog.Render([template]));
        JsonElement item = document.RootElement.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(4u, item.GetProperty("damageMin1").GetUInt32());
        Assert.Equal(9u, item.GetProperty("damageMax1").GetUInt32());
        Assert.Equal((int)DamageType.Physical, item.GetProperty("damageType1").GetInt32());
        Assert.Equal((int)StatType.Strength, item.GetProperty("statType1").GetInt32());
        Assert.Equal(7u, item.GetProperty("statValue1").GetUInt32());
    }

    /// <summary>
    /// A field that is genuinely absent (never set) stays absent rather than becoming a literal
    /// null in the file -- the same DefaultIgnoreCondition that must not swallow Name is relied on
    /// here for every field that really is optional.
    /// </summary>
    [Fact]
    public void Omit_Unset_Optional_Fields_Rather_Than_Writing_Null()
    {
        using var document = JsonDocument.Parse(ItemCatalog.Render([Template(1, "x")]));
        JsonElement item = document.RootElement.GetProperty("items").EnumerateArray().Single();

        Assert.False(item.TryGetProperty("slot", out _));
        Assert.False(item.TryGetProperty("damageMin1", out _));
    }

    /// <summary>
    /// item-schema-v1.json describes AllowedClasses as an array of the numeric CharacterClass
    /// vocabulary, matching every other enum field the catalog emits (class, subClass, rarity,
    /// flags, damageType1, statType1). CharacterClass itself carries
    /// [JsonConverter(typeof(JsonStringEnumConverter))] for Avalon.Api's REST responses, which
    /// ItemCatalog.Render's reflection-driven serializer would otherwise inherit -- a type-level
    /// [JsonConverter] attribute beats anything in JsonSerializerOptions.Converters, so a client
    /// following the schema would parse an int[] and throw on the first item.
    /// </summary>
    [Fact]
    public void Render_AllowedClasses_As_Numbers_Not_Strings()
    {
        ItemTemplate template = Template(1, "x");
        template.AllowedClasses = [CharacterClass.Warrior, CharacterClass.Healer];

        using var document = JsonDocument.Parse(ItemCatalog.Render([template]));
        JsonElement item = document.RootElement.GetProperty("items").EnumerateArray().Single();
        JsonElement allowedClasses = item.GetProperty("allowedClasses");

        Assert.Equal(JsonValueKind.Array, allowedClasses.ValueKind);
        foreach (JsonElement element in allowedClasses.EnumerateArray())
            Assert.Equal(JsonValueKind.Number, element.ValueKind);

        Assert.Equal(
            [(int)CharacterClass.Warrior, (int)CharacterClass.Healer],
            allowedClasses.EnumerateArray().Select(element => element.GetInt32()));
    }
}

/// <summary>
/// Where the item-catalog export finds its World connection string (#557): the environment or the
/// World database project's user-secrets, the design-time factories' sources, and never an
/// appsettings file in the working directory, which once let it connect to whatever database a
/// local file named. These set the real working directory and a real process variable, so they run
/// alone. Nothing here connects to a database.
/// </summary>
[CollectionDefinition(nameof(ItemCatalogConnectionShould), DisableParallelization = true)]
public sealed class ItemCatalogConnectionCollection
{
}

[Collection(nameof(ItemCatalogConnectionShould))]
public class ItemCatalogConnectionShould
{
    private const string Variable = "Database__World__ConnectionString";
    private const string FromFile = "Host=127.0.0.1;Port=1;Database=from_working_directory_file";
    private const string FromEnvironment = "Host=127.0.0.1;Port=1;Database=from_environment";

    // The test assembly declares no UserSecretsId, so a developer's own user-secrets for the World
    // database project cannot decide these tests; production reads that project's.
    private static readonly System.Reflection.Assembly s_noUserSecrets = typeof(ItemCatalogConnectionShould).Assembly;

    private static void InWorkingDirectoryWithAppsettings(string? variable, Action body)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        string? before = Environment.GetEnvironmentVariable(Variable);
        string folder = Path.Combine(Path.GetTempPath(), "avalon-557-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string settings = "{ \"Database\": { \"World\": { \"ConnectionString\": \"" + FromFile + "\" } } }";
        File.WriteAllText(Path.Combine(folder, "appsettings.json"), settings);
        File.WriteAllText(Path.Combine(folder, "appsettings.Design.json"), settings);
        try
        {
            Directory.SetCurrentDirectory(folder);
            Environment.SetEnvironmentVariable(Variable, variable);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, before);
            Directory.SetCurrentDirectory(workingDirectory);
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Not_Read_An_Appsettings_File_In_The_Working_Directory() =>
        InWorkingDirectoryWithAppsettings(variable: null, () =>
        {
            string? connectionString = ItemCatalog.ConnectionString(s_noUserSecrets);

            Assert.Null(connectionString);
            string? reason = ItemCatalog.ReadinessFor(connectionString);
            Assert.NotNull(reason);
            Assert.Contains(Variable, reason, StringComparison.Ordinal);
        });

    [Fact]
    public void Use_The_Connection_String_The_Environment_Names() =>
        InWorkingDirectoryWithAppsettings(FromEnvironment, () =>
        {
            string? connectionString = ItemCatalog.ConnectionString(s_noUserSecrets);

            Assert.Equal(FromEnvironment, connectionString);
            Assert.Null(ItemCatalog.ReadinessFor(connectionString));
        });

    [Fact]
    public void Treat_A_Blank_Environment_Value_As_Not_Configured() =>
        InWorkingDirectoryWithAppsettings("   ", () =>
            Assert.Null(ItemCatalog.ConnectionString(s_noUserSecrets)));
}
