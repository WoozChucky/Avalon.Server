using Avalon.Database.World;
using Avalon.Database.World.Migrations;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// #163: AbilityTemplates.WeaponCoefficient became BaseDamageCoefficient and SpellScript became ScriptName.
/// SQLite builds the model with EnsureCreated and never runs the migration, so its operations are read
/// directly: a rename keeps every row's value, a drop and add would lose them.
/// </summary>
public class RenameAbilityScriptAndBaseDamageMigrationShould
{
    [Theory]
    [InlineData("WeaponCoefficient", "BaseDamageCoefficient")]
    [InlineData("SpellScript", "ScriptName")]
    public void Rename_the_column_rather_than_drop_and_add_it(string from, string to)
    {
        List<MigrationOperation> up = [.. new RenameAbilityScriptAndBaseDamage().UpOperations];

        Assert.Contains(up, o => o is RenameColumnOperation { Table: "AbilityTemplates" } r
                                 && r.Name == from && r.NewName == to);
        Assert.DoesNotContain(up, o => o is DropColumnOperation or AddColumnOperation);
    }

    [Fact]
    public void Map_the_renamed_properties_to_the_renamed_columns()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        IEntityType entity = context.Model.FindEntityType(typeof(AbilityTemplate))!;
        var table = StoreObjectIdentifier.Table("AbilityTemplates", schema: null);

        Assert.Equal("BaseDamageCoefficient",
            entity.FindProperty(nameof(AbilityTemplate.BaseDamageCoefficient))!.GetColumnName(table));
        Assert.Equal("ScriptName", entity.FindProperty(nameof(AbilityTemplate.ScriptName))!.GetColumnName(table));
    }
}
