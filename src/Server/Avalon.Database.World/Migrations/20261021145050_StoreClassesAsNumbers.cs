using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <summary>
    /// Stores the two class columns that held class names as the class numbers, as every other class column is
    /// (#921, #922, #1142): <c>ItemTemplates.AllowedClasses</c> (comma-joined names, text) becomes <c>smallint[]</c>
    /// and <c>CharacterClassNames.Class</c> (the key, a name) becomes <c>smallint</c>.
    /// </summary>
    /// <remarks>
    /// Hand-written over the generated migration, which deleted and re-seeded the rows: every row is converted in
    /// place instead, so an item template edited live (PUT) keeps its classes and a row the seed does not hold is
    /// converted too. A name the conversion does not know fails the cast, and with it the migration, rather than being
    /// dropped. The conversion is element by element, in order, so an item's class order is kept.
    /// </remarks>
    public partial class StoreClassesAsNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""ItemTemplates"" ALTER COLUMN ""AllowedClasses"" TYPE smallint[] USING
    array_replace(array_replace(array_replace(array_replace(array_replace(
        string_to_array(""AllowedClasses"", ','),
        'None', '0'), 'Warrior', '1'), 'Wizard', '2'), 'Hunter', '3'), 'Healer', '4')::smallint[];");

            migrationBuilder.Sql(@"
ALTER TABLE ""CharacterClassNames"" ALTER COLUMN ""Class"" TYPE smallint USING
    CASE ""Class""
        WHEN 'None' THEN 0 WHEN 'Warrior' THEN 1 WHEN 'Wizard' THEN 2 WHEN 'Hunter' THEN 3 WHEN 'Healer' THEN 4
        ELSE ""Class""::smallint
    END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""ItemTemplates"" ALTER COLUMN ""AllowedClasses"" TYPE text USING
    array_to_string(
        array_replace(array_replace(array_replace(array_replace(array_replace(
            ""AllowedClasses""::text[],
            '0', 'None'), '1', 'Warrior'), '2', 'Wizard'), '3', 'Hunter'), '4', 'Healer'),
        ',');");

            migrationBuilder.Sql(@"
ALTER TABLE ""CharacterClassNames"" ALTER COLUMN ""Class"" TYPE text USING
    CASE ""Class""
        WHEN 0 THEN 'None' WHEN 1 THEN 'Warrior' WHEN 2 THEN 'Wizard' WHEN 3 THEN 'Hunter' WHEN 4 THEN 'Healer'
        ELSE ""Class""::text
    END;");
        }
    }
}
