using Avalon.Database.Character.Migrations;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// #1179 (B-R13): the Character migration that gives every copy owned before implicits existed its base's implicit at the
/// drafted value. Its table is frozen code, copied from the World seed as plain values, so it is held to that seed here.
/// </summary>
public class ImplicitBackfillShould
{
    /// <summary>
    /// Row for row: each base's drafted lines in order, under its implicit's seeded modifier id (looked up by template)
    /// and the line's index, with the drafted value. A table that drifted would give owned copies other defence than the
    /// seed drafted, or name a modifier the catalog does not hold as that base's.
    /// </summary>
    [Fact]
    public void Match_the_seeded_implicits()
    {
        (ulong, int, byte, StatType, int)[] seeded =
        [
            .. ItemModifierSeed.Implicits().GroupBy(line => line.TemplateId).SelectMany(template => template.Select((line, index) =>
                (template.Key, CatalogKeys.ModifierId($"implicit {template.Key}"), (byte)index, line.Stat, line.Value))),
        ];

        Assert.Equal(seeded, AddImplicitsToOwnedItems.Implicits);
    }
}
