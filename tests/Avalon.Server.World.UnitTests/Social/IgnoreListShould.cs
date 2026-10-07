using Avalon.Database.Character.Repositories;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;

namespace Avalon.Server.World.UnitTests.Social;

/// <summary>
/// IgnoreList (#723) holds one character's ignore list in memory. Every change marks the save; loading does not.
/// </summary>
public class IgnoreListShould
{
    private static readonly DateTime s_now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static CharacterEntity Character() => TestCharacters.New(1);

    [Fact]
    public void Load_in_order_without_marking_the_save()
    {
        CharacterEntity c = Character();

        c.Ignores.Load([new IgnoredCharacterRow(7, "Kaela", s_now), new IgnoredCharacterRow(3, "Tom", s_now.AddMinutes(1))]);

        Assert.Equal([7u, 3u], c.Ignores.Entries.Select(e => e.Id));
        Assert.True(c.Ignores.Contains(7));
        Assert.False(c.Ignores.Contains(2));
        Assert.False(c.SaveState.HasChanges);
    }

    [Fact]
    public void Replace_what_it_held_on_load()
    {
        CharacterEntity c = Character();
        c.Ignores.Add(9, "Old", s_now);

        c.Ignores.Load([new IgnoredCharacterRow(7, "Kaela", s_now)]);

        Assert.False(c.Ignores.Contains(9));
        Assert.Equal(1, c.Ignores.Count);
    }

    [Fact]
    public void Add_a_character_only_once()
    {
        CharacterEntity c = Character();
        c.Ignores.Add(7, "Kaela", s_now);

        Assert.False(c.Ignores.Add(7, "Kaela", s_now));

        Assert.Equal(1, c.Ignores.Count);
    }

    [Theory]
    [InlineData("Kaela")]
    [InlineData("kAELA")]
    [InlineData("  kaela ")]
    public void Find_an_entry_by_name_ignoring_case_and_spaces(string name)
    {
        CharacterEntity c = Character();
        c.Ignores.Add(7, "Kaela", s_now);

        Assert.Equal(7u, c.Ignores.FindByName(name)!.Id);
        Assert.Null(c.Ignores.FindByName("Tom"));
    }

    /// <summary>Only ASCII letters fold, as the stored key does (#757): a dotless i is not an i.</summary>
    [Fact]
    public void Find_no_entry_by_a_name_that_matches_only_outside_ascii()
    {
        CharacterEntity c = Character();
        c.Ignores.Add(7, "Bill", s_now);

        Assert.Null(c.Ignores.FindByName("Bıll"));
    }
}
