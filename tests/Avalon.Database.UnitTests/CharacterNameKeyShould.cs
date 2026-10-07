using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Migrations;
using Avalon.Database.Character.Repositories;
using Avalon.World.Public.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.UnitTests;

/// <summary>
/// Character names are one name whatever their case (#757): <c>Characters.NameKey</c> holds the upper-cased name under
/// a unique index, a check constraint holds it to <c>upper("Name")</c>, and every lookup by name goes through it.
/// </summary>
public class CharacterNameKeyShould
{
    [Fact]
    public void Derive_the_key_from_the_name()
    {
        CharacterRow row = new() { Name = "Kaela" };
        Assert.Equal("KAELA", row.NameKey);

        row.Name = "Bob";
        Assert.Equal("BOB", row.NameKey);
        Assert.Equal("BOB", row.Copy().NameKey);
    }

    [Fact]
    public async Task Refuse_a_second_character_whose_name_differs_only_in_case()
    {
        using var database = SqliteDatabase.Characters();
        await using (CharacterDbContext write = database.CreateDbContext())
        {
            write.Characters.Add(Row(1, "Bob"));
            await write.SaveChangesAsync();
        }

        await using CharacterDbContext second = database.CreateDbContext();
        second.Characters.Add(Row(2, "BOB"));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Refuse_a_row_whose_key_is_not_its_upper_cased_name()
    {
        using var database = SqliteDatabase.Characters();
        await using (CharacterDbContext write = database.CreateDbContext())
        {
            write.Characters.Add(Row(1, "Bob"));
            await write.SaveChangesAsync();
        }

        await using CharacterDbContext update = database.CreateDbContext();
        SqliteException refused = await Assert.ThrowsAsync<SqliteException>(() =>
            update.Characters.ExecuteUpdateAsync(u => u.SetProperty(c => c.NameKey, "ALICE")));
        Assert.Contains(CharacterDbContext.NameKeyConstraint, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Kaela")]
    [InlineData("kaela")]
    [InlineData("KAELA")]
    [InlineData(" kAeLa ")]
    public async Task Find_a_character_by_its_name_in_any_case(string typed)
    {
        using var database = SqliteDatabase.Characters();
        await using (CharacterDbContext write = database.CreateDbContext())
        {
            write.Characters.Add(Row(7, "Kaela"));
            await write.SaveChangesAsync();
        }

        CharacterRow? found = await new CharacterRepository(database).FindByNameAsync(typed);
        Assert.Equal(7u, found?.Id.Value);

        CharacterNameMatch? match = await new CharacterIgnoreRepository(database).FindCharacterByNameAsync(typed);
        Assert.Equal(new CharacterNameMatch(7u, "Kaela"), match);
    }

    [Fact]
    public async Task Find_no_character_for_a_name_that_folds_into_another_only_outside_ascii()
    {
        using var database = SqliteDatabase.Characters();
        await using (CharacterDbContext write = database.CreateDbContext())
        {
            write.Characters.Add(Row(1, "Bill"));
            await write.SaveChangesAsync();
        }

        Assert.Null(await new CharacterRepository(database).FindByNameAsync("Bıll"));
        Assert.Null(await new CharacterIgnoreRepository(database).FindCharacterByNameAsync("Bıll"));
    }

    /// <summary>
    /// The migration's backfill, run against a Characters table as it stood before it (no key yet, so the column
    /// starts empty): every existing row gets its upper-cased name. SQLite's <c>upper()</c> folds ASCII as Postgres
    /// does, which is all the rule allows.
    /// </summary>
    [Fact]
    public async Task Fill_the_key_of_every_existing_character_in_the_migration()
    {
        string fill = new AddCharacterNameKey().UpOperations.OfType<SqlOperation>().Single().Sql;

        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        // The fill folds with COLLATE "C", a Postgres collation; SQLite needs it registered to run the statement.
        connection.CreateCollation("C", (x, y) => string.CompareOrdinal(x, y));
        await Execute(connection, """CREATE TABLE "Characters" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL, "NameKey" TEXT NOT NULL DEFAULT '')""");
        await Execute(connection, """INSERT INTO "Characters" ("Id", "Name") VALUES (1, 'Kaela'), (2, 'bob'), (3, 'ALICE')""");

        await Execute(connection, fill);

        await using SqliteCommand read = connection.CreateCommand();
        read.CommandText = """SELECT "NameKey" FROM "Characters" ORDER BY "Id" """;
        List<string> keys = [];
        await using (SqliteDataReader reader = await read.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                keys.Add(reader.GetString(0));
        }

        Assert.Equal(["KAELA", "BOB", "ALICE"], keys);
    }

    /// <summary>The backfill runs before the unique index is built: on the empty default every row would clash.</summary>
    [Fact]
    public void Fill_the_key_before_building_its_unique_index()
    {
        var ops = new AddCharacterNameKey().UpOperations.ToList();

        int add = ops.FindIndex(o => o is AddColumnOperation { Name: "NameKey" });
        int fill = ops.FindIndex(o => o is SqlOperation);
        int index = ops.FindIndex(o => o is CreateIndexOperation { IsUnique: true } c && c.Columns.SequenceEqual(["NameKey"]));
        int check = ops.FindIndex(o => o is AddCheckConstraintOperation { Name: CharacterDbContext.NameKeyConstraint });

        Assert.True(add >= 0 && fill > add && index > fill && check > fill,
            $"add {add}, fill {fill}, index {index}, check {check}");
    }

    /// <summary>
    /// No tracked update writes the name: the world's whole-row saves, the select-time write and the API's admin patch
    /// cannot put an older name back over a rename, nor fail on the unique index once another character took it.
    /// </summary>
    [Fact]
    public async Task Leave_the_name_out_of_every_tracked_update()
    {
        using var database = SqliteDatabase.Characters();
        await using (CharacterDbContext write = database.CreateDbContext())
        {
            write.Characters.Add(Row(1, "Kaela"));
            await write.SaveChangesAsync();
        }

        CharacterRow stale = Row(1, "Oldname");
        stale.Level = 7;
        await new CharacterRepository(database).UpdateAsync(stale);

        await using CharacterDbContext read = database.CreateDbContext();
        CharacterRow stored = await read.Characters.AsNoTracking().SingleAsync();
        Assert.Equal(("Kaela", "KAELA", (ushort)7), (stored.Name, stored.NameKey, stored.Level));
    }

    [Fact]
    public async Task Rename_an_offline_character()
    {
        using var database = SqliteDatabase.Characters();
        await StoreAsync(database, Row(1, "Kaela"));

        Assert.Equal(CharacterRename.Renamed, await new CharacterRepository(database).TryRenameAsync(1u, "Borin"));

        await using CharacterDbContext read = database.CreateDbContext();
        CharacterRow stored = await read.Characters.AsNoTracking().SingleAsync();
        Assert.Equal(("Borin", "BORIN"), (stored.Name, stored.NameKey));
    }

    [Fact]
    public async Task Refuse_to_rename_an_online_character_and_write_nothing()
    {
        using var database = SqliteDatabase.Characters();
        CharacterRow online = Row(1, "Kaela");
        online.Online = true;
        await StoreAsync(database, online);

        Assert.Equal(CharacterRename.Online, await new CharacterRepository(database).TryRenameAsync(1u, "Borin"));

        await using CharacterDbContext read = database.CreateDbContext();
        Assert.Equal("Kaela", (await read.Characters.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task Answer_a_rename_onto_a_taken_key_as_name_taken()
    {
        using var database = SqliteDatabase.Characters();
        await StoreAsync(database, Row(1, "Kaela"));
        await StoreAsync(database, Row(2, "Borin"));

        Assert.Equal(CharacterRename.NameTaken, await new CharacterRepository(database).TryRenameAsync(1u, "Borin"));
    }

    [Fact]
    public async Task Answer_a_rename_of_no_character_as_not_found()
    {
        using var database = SqliteDatabase.Characters();
        Assert.Equal(CharacterRename.NotFound, await new CharacterRepository(database).TryRenameAsync(9u, "Borin"));
    }

    [Fact]
    public async Task Recognise_only_the_unique_violation_on_the_name_key()
    {
        using var database = SqliteDatabase.Characters();
        await StoreAsync(database, Row(1, "Bob"));

        DbUpdateException nameClash = await Assert.ThrowsAsync<DbUpdateException>(() => StoreAsync(database, Row(2, "BOB")));
        DbUpdateException idClash = await Assert.ThrowsAsync<DbUpdateException>(() => StoreAsync(database, Row(1, "Alice")));

        Assert.True(CharacterNameKeyViolation.Is(nameClash));
        Assert.False(CharacterNameKeyViolation.Is(idClash));
        Assert.False(CharacterNameKeyViolation.Is(new DbUpdateException("something else")));
    }

    private static async Task StoreAsync(SqliteDatabase<CharacterDbContext> database, CharacterRow row)
    {
        await using CharacterDbContext write = database.CreateDbContext();
        write.Characters.Add(row);
        await write.SaveChangesAsync();
    }

    private static async Task Execute(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static CharacterRow Row(uint id, string name) => new()
    {
        Id = new CharacterId(id),
        AccountId = new AccountId(1),
        Name = name,
        Class = CharacterClass.Warrior,
        CreationDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
    };
}
