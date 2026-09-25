using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Migrations;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.UnitTests;

/// <summary>
/// Everything a player owns lives in the Character database: the gold on
/// the character row, the item instances, and the slots that place them, now joined by a real
/// foreign key. The World database keeps templates only.
/// </summary>
public class CharacterDbContextShould
{
    [Fact]
    public async Task Round_trip_a_characters_money()
    {
        using SqliteDatabase<CharacterDbContext> database = SqliteDatabase.Characters();
        await using (CharacterDbContext write = database.CreateDbContext())
        {
            write.Characters.Add(NewCharacter(money: 12_345_678_901UL));
            await write.SaveChangesAsync();
        }

        await using CharacterDbContext read = database.CreateDbContext();
        Assert.Equal(12_345_678_901UL, (await read.Characters.AsNoTracking().SingleAsync()).Money);
    }

    [Fact]
    public async Task Start_a_character_with_no_money()
    {
        Assert.Equal(0UL, new CharacterRow().Money);

        using SqliteDatabase<CharacterDbContext> database = SqliteDatabase.Characters();
        await using (CharacterDbContext write = database.CreateDbContext())
        {
            write.Characters.Add(NewCharacter());
            await write.SaveChangesAsync();
        }

        await using CharacterDbContext read = database.CreateDbContext();
        Assert.Equal(0UL, (await read.Characters.AsNoTracking().SingleAsync()).Money);
    }

    [Fact]
    public async Task Round_trip_an_item_instance_and_the_slot_that_holds_it()
    {
        using SqliteDatabase<CharacterDbContext> database = WithForeignKeys();
        var itemId = new ItemInstanceId(Guid.CreateVersion7());
        CharacterId owner;

        await using (CharacterDbContext write = database.CreateDbContext())
        {
            CharacterRow character = NewCharacter();
            write.Characters.Add(character);
            await write.SaveChangesAsync();
            owner = character.Id;

            write.ItemInstances.Add(new ItemInstance
            {
                Id = itemId, TemplateId = new ItemTemplateId(4242), CharacterId = owner,
                Count = 7, Durability = 33, Charges = 2, Flags = ItemInstanceFlags.Broken,
                UpdatedAt = DateTime.UtcNow,
            });
            write.CharacterInventory.Add(new CharacterInventory
            {
                CharacterId = owner, Container = InventoryType.Bag, Slot = 3, ItemId = itemId,
            });
            await write.SaveChangesAsync();
        }

        await using CharacterDbContext read = database.CreateDbContext();
        ItemInstance item = await read.ItemInstances.AsNoTracking().SingleAsync();
        Assert.Equal(itemId, item.Id);
        Assert.Equal(new ItemTemplateId(4242), item.TemplateId);
        Assert.Equal(owner, item.CharacterId);
        Assert.Equal(7u, item.Count);
        Assert.Equal(33u, item.Durability);
        Assert.Equal(2u, item.Charges);
        Assert.Equal(ItemInstanceFlags.Broken, item.Flags);

        CharacterInventory slot = await read.CharacterInventory.AsNoTracking().SingleAsync();
        Assert.Equal(itemId, slot.ItemId);
        Assert.Equal((ushort)3, slot.Slot);
    }

    [Fact]
    public async Task Refuse_a_slot_whose_item_does_not_exist()
    {
        using SqliteDatabase<CharacterDbContext> database = WithForeignKeys();
        await using CharacterDbContext context = database.CreateDbContext();
        CharacterRow character = NewCharacter();
        context.Characters.Add(character);
        await context.SaveChangesAsync();

        context.CharacterInventory.Add(new CharacterInventory
        {
            CharacterId = character.Id, Container = InventoryType.Bag, Slot = 0,
            ItemId = new ItemInstanceId(Guid.CreateVersion7()),
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void Delete_a_slot_together_with_the_item_it_holds()
    {
        using SqliteDatabase<CharacterDbContext> database = SqliteDatabase.Characters();
        using CharacterDbContext context = database.CreateDbContext();

        IForeignKey foreignKey = context.Model.FindEntityType(typeof(CharacterInventory))!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(ItemInstance));

        Assert.Equal(nameof(CharacterInventory.ItemId), Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Leave_item_ids_to_the_server()
    {
        using SqliteDatabase<CharacterDbContext> database = SqliteDatabase.Characters();
        using CharacterDbContext context = database.CreateDbContext();

        IProperty id = context.Model.FindEntityType(typeof(ItemInstance))!.FindProperty(nameof(ItemInstance.Id))!;

        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
    }

    /// <summary>The template id is a plain reference into the World database; nothing pulls templates in here.</summary>
    [Fact]
    public void Keep_item_templates_out_of_the_character_database()
    {
        using SqliteDatabase<CharacterDbContext> database = SqliteDatabase.Characters();
        using CharacterDbContext context = database.CreateDbContext();

        Assert.Null(context.Model.FindEntityType(typeof(ItemTemplate)));
    }

    [Fact]
    public void Stop_mapping_item_instances_in_the_world_database()
    {
        using SqliteDatabase<World.WorldDbContext> database = SqliteDatabase.World();
        using World.WorldDbContext context = database.CreateDbContext();

        Assert.Null(context.Model.FindEntityType(typeof(ItemInstance)));
    }

    /// <summary>
    /// The foreign key cannot be added while slot rows point at items that are not in this
    /// database, which on the first run is all of them. The delete has to come first.
    /// </summary>
    [Fact]
    public void Delete_orphaned_slots_before_adding_the_foreign_key()
    {
        var migration = new AddMoneyAndItemInstances { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };
        List<MigrationOperation> operations = migration.UpOperations.ToList();

        int delete = operations.FindIndex(op =>
            op is SqlOperation sql && sql.Sql.Contains("DELETE FROM \"CharacterInventory\"", StringComparison.Ordinal));
        int foreignKey = operations.FindIndex(op =>
            op is AddForeignKeyOperation fk && fk.Table == "CharacterInventory" && fk.PrincipalTable == "ItemInstances");

        Assert.True(delete >= 0, "the migration does not delete orphaned CharacterInventory rows");
        Assert.True(foreignKey >= 0, "the migration does not add the CharacterInventory -> ItemInstances foreign key");
        Assert.True(delete < foreignKey, "the orphan delete must run before the foreign key is added");
    }

    private static SqliteDatabase<CharacterDbContext> WithForeignKeys()
    {
        SqliteDatabase<CharacterDbContext> database = SqliteDatabase.Characters();
        using CharacterDbContext context = database.CreateDbContext();
        // Test-only, on the in-memory SQLite connection: makes enforcement explicit rather than a
        // property of how the bundled SQLite happened to be compiled.
        context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        return database;
    }

    private static CharacterRow NewCharacter(ulong money = 0) => new()
    {
        AccountId = new AccountId(1),
        Name = "Probe",
        Class = CharacterClass.Warrior,
        Money = money,
        CreationDate = DateTime.UtcNow,
    };
}
