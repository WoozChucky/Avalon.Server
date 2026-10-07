using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.World.Public.Enums;
using Xunit;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.UnitTests;

/// <summary>
/// An account's characters come back oldest first (#727): by creation time, then by id when two
/// share a creation time. Without an order the database returns rows as it scans them, and on
/// Postgres every save moves the row, so the in-game list reshuffled between logins.
/// </summary>
public class CharacterRepositoryShould
{
    private static readonly AccountId s_owner = new(1);
    private static readonly DateTime s_day = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task List_an_accounts_characters_oldest_first_whatever_order_they_were_inserted_in()
    {
        using var database = SqliteDatabase.Characters();
        await using (CharacterDbContext write = database.CreateDbContext())
        {
            // Ids rise in insert order, opposite to creation, so ordering by id alone would fail.
            write.Characters.AddRange(
                Row(1, "Newest", s_day.AddDays(3)),
                Row(2, "Oldest", s_day),
                Row(3, "Middle", s_day.AddDays(1)),
                Row(4, "Stranger", s_day.AddDays(-5), new AccountId(2)));
            await write.SaveChangesAsync();
        }

        List<CharacterRow> found = await new CharacterRepository(database).FindByAccountAsync(s_owner);

        Assert.Equal(["Oldest", "Middle", "Newest"], found.Select(c => c.Name));
    }

    [Fact]
    public async Task Break_a_creation_time_tie_by_id()
    {
        using var database = SqliteDatabase.Characters();
        await using (CharacterDbContext write = database.CreateDbContext())
        {
            // Inserted highest id first, all three created at the same instant.
            write.Characters.Add(Row(30, "Third", s_day));
            await write.SaveChangesAsync();
            write.Characters.Add(Row(10, "First", s_day));
            await write.SaveChangesAsync();
            write.Characters.Add(Row(20, "Second", s_day));
            await write.SaveChangesAsync();
        }

        List<CharacterRow> found = await new CharacterRepository(database).FindByAccountAsync(s_owner);

        Assert.Equal(["First", "Second", "Third"], found.Select(c => c.Name));
    }

    private static CharacterRow Row(uint id, string name, DateTime created, AccountId? account = null) => new()
    {
        Id = new CharacterId(id),
        AccountId = account ?? s_owner,
        Name = name,
        Class = CharacterClass.Warrior,
        CreationDate = created,
    };
}
