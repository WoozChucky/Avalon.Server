using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <summary>
    /// #1179: a base's defence moved from its template to its implicit modifier (the World migration
    /// MoveBaseStatsToImplicits), so every copy of one of the 60 defence-bearing bases owned before then gets its implicit
    /// here: one ItemInstanceModifiers row (its base's implicit modifier, ordinal 0) and a value row per line at the
    /// drafted value, the defence the copy had, so nothing loses any. Copies made from then on roll theirs when made. No
    /// copy had modifier rows before this part; one that has any (made by a world already running this part) is left as
    /// it is. Row ids come from gen_random_uuid(): this data migration is the one place the database makes a modifier id.
    /// Down deletes every modifier row naming one of the table's modifiers (their values go by cascade); the Character
    /// database cannot read the World's catalog, so the table is the list. No model change.
    /// </summary>
    public partial class AddImplicitsToOwnedItems : Migration
    {
        // The stats' stored values (StatType, smallint) as literals: a migration is frozen, and must write the same rows
        // whatever the enum becomes.
        private const short Armor = 4, BlockPct = 5, Evasion = 6, Ward = 14, IncreasedSpellDamage = 26;

        /// <summary>
        /// Each base's implicit as drafted: its template, its implicit modifier's seeded id, and each line's index, stat and
        /// drafted value, in template and line order. Frozen copies of ItemModifierSeed's values (a migration never reads
        /// the seed, which is another database's), the stats as their stored numbers; ImplicitBackfillShould holds them equal.
        /// </summary>
        public static readonly (ulong TemplateId, int ModifierId, byte Index, short Stat, int Value)[] Implicits =
        [
            (12, 224, 0, Armor, 11),
            (13, 225, 0, Armor, 18),
            (14, 226, 0, Armor, 14),
            (15, 227, 0, Armor, 7),
            (16, 228, 0, Armor, 7),
            (17, 229, 0, Ward, 16),
            (18, 230, 0, Ward, 26),
            (19, 231, 0, Ward, 21),
            (20, 232, 0, Ward, 10),
            (21, 233, 0, Ward, 10),
            (22, 234, 0, Evasion, 54),
            (23, 235, 0, Evasion, 90),
            (24, 236, 0, Evasion, 72),
            (25, 237, 0, Evasion, 36),
            (26, 238, 0, Evasion, 36),
            (36, 239, 0, Armor, 5),
            (37, 240, 0, Armor, 8),
            (38, 241, 0, Armor, 6),
            (39, 242, 0, Armor, 3),
            (40, 243, 0, Armor, 3),
            (41, 244, 0, Ward, 7),
            (42, 245, 0, Ward, 12),
            (43, 246, 0, Ward, 10),
            (44, 247, 0, Ward, 5),
            (45, 248, 0, Ward, 5),
            (46, 249, 0, Evasion, 24),
            (47, 250, 0, Evasion, 40),
            (48, 251, 0, Evasion, 32),
            (49, 252, 0, Evasion, 16),
            (50, 253, 0, Evasion, 16),
            (70, 254, 0, Armor, 6),
            (70, 254, 1, BlockPct, 5),
            (71, 255, 0, Armor, 14),
            (71, 255, 1, BlockPct, 7),
            (72, 256, 0, Armor, 24),
            (72, 256, 1, BlockPct, 10),
            (73, 257, 0, Ward, 8),
            (73, 257, 1, IncreasedSpellDamage, 5),
            (74, 258, 0, Ward, 18),
            (74, 258, 1, IncreasedSpellDamage, 8),
            (75, 259, 0, Ward, 30),
            (75, 259, 1, IncreasedSpellDamage, 12),
            (76, 260, 0, Armor, 4),
            (77, 261, 0, Evasion, 20),
            (78, 262, 0, Ward, 6),
            (79, 263, 0, Armor, 9),
            (80, 264, 0, Evasion, 45),
            (81, 265, 0, Ward, 13),
            (82, 266, 0, Armor, 16),
            (83, 267, 0, Evasion, 80),
            (84, 268, 0, Ward, 23),
            (85, 269, 0, Armor, 19),
            (86, 270, 0, Armor, 32),
            (87, 271, 0, Armor, 26),
            (88, 272, 0, Armor, 13),
            (89, 273, 0, Armor, 13),
            (90, 274, 0, Evasion, 96),
            (91, 275, 0, Evasion, 160),
            (92, 276, 0, Evasion, 128),
            (93, 277, 0, Evasion, 64),
            (94, 278, 0, Evasion, 64),
            (95, 279, 0, Ward, 27),
            (96, 280, 0, Ward, 45),
            (97, 281, 0, Ward, 36),
            (98, 282, 0, Ward, 18),
            (99, 283, 0, Ward, 18),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            string implicits = string.Join(", ", Implicits.Select(row => string.Create(CultureInfo.InvariantCulture,
                $"({row.TemplateId}, {row.ModifierId}, {row.Index}, {row.Stat}, {row.Value})")));

            // One statement: the modifier rows are inserted for every owned copy of a listed base that has none yet (a copy a
            // world running this part made already carries its rolled implicit, whichever database migrated first), and
            // their value rows from what that insert returns, joined back to the table by the copy's template.
            migrationBuilder.Sql($"""
                WITH "Implicit" ("TemplateId", "ModifierId", "Index", "Stat", "Value") AS (VALUES
                    {implicits}),
                "Added" AS (
                    INSERT INTO "ItemInstanceModifiers" ("Id", "ItemInstanceId", "ModifierId", "Ordinal")
                    SELECT gen_random_uuid(), i."Id", b."ModifierId", 0
                    FROM "ItemInstances" AS i
                    JOIN (SELECT DISTINCT "TemplateId", "ModifierId" FROM "Implicit") AS b ON b."TemplateId" = i."TemplateId"
                    WHERE NOT EXISTS (SELECT 1 FROM "ItemInstanceModifiers" AS m WHERE m."ItemInstanceId" = i."Id")
                    RETURNING "Id", "ItemInstanceId", "ModifierId")
                INSERT INTO "ItemInstanceModifierValues" ("ItemInstanceModifierId", "Index", "Stat", "Value", "Value2")
                SELECT a."Id", v."Index", v."Stat", v."Value", NULL
                FROM "Added" AS a
                JOIN "ItemInstances" AS i ON i."Id" = a."ItemInstanceId"
                JOIN "Implicit" AS v ON v."TemplateId" = i."TemplateId" AND v."ModifierId" = a."ModifierId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string modifiers = string.Join(", ", Implicits.Select(row => row.ModifierId).Distinct()
                .Select(id => id.ToString(CultureInfo.InvariantCulture)));
            migrationBuilder.Sql($"DELETE FROM \"ItemInstanceModifiers\" WHERE \"ModifierId\" IN ({modifiers});");
        }
    }
}
