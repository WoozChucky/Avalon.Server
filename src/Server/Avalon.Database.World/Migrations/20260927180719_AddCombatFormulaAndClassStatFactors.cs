using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCombatFormulaAndClassStatFactors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassStatFactors",
                columns: table => new
                {
                    Class = table.Column<int>(type: "integer", nullable: false),
                    HpPerStamina = table.Column<long>(type: "bigint", nullable: false),
                    PowerPerIntellect = table.Column<double>(type: "double precision", nullable: false),
                    PowerPerAgility = table.Column<double>(type: "double precision", nullable: false),
                    FixedPower = table.Column<long>(type: "bigint", nullable: true),
                    AttackPerStrength = table.Column<double>(type: "double precision", nullable: false),
                    AttackPerAgility = table.Column<double>(type: "double precision", nullable: false),
                    AbilityPerIntellect = table.Column<double>(type: "double precision", nullable: false),
                    BaseBlock = table.Column<float>(type: "real", nullable: false),
                    BaseDodge = table.Column<float>(type: "real", nullable: false),
                    BaseCrit = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassStatFactors", x => x.Class);
                    table.CheckConstraint("CK_ClassStatFactors_AbilityPerIntellect", "\"AbilityPerIntellect\" >= 0 AND \"AbilityPerIntellect\" < 'Infinity'");
                    table.CheckConstraint("CK_ClassStatFactors_AttackPerAgility", "\"AttackPerAgility\" >= 0 AND \"AttackPerAgility\" < 'Infinity'");
                    table.CheckConstraint("CK_ClassStatFactors_AttackPerStrength", "\"AttackPerStrength\" >= 0 AND \"AttackPerStrength\" < 'Infinity'");
                    table.CheckConstraint("CK_ClassStatFactors_BaseBlock", "\"BaseBlock\" >= 0 AND \"BaseBlock\" < 'Infinity'");
                    table.CheckConstraint("CK_ClassStatFactors_BaseCrit", "\"BaseCrit\" >= 0 AND \"BaseCrit\" < 'Infinity'");
                    table.CheckConstraint("CK_ClassStatFactors_BaseDodge", "\"BaseDodge\" >= 0 AND \"BaseDodge\" < 'Infinity'");
                    table.CheckConstraint("CK_ClassStatFactors_PowerPerAgility", "\"PowerPerAgility\" >= 0 AND \"PowerPerAgility\" < 'Infinity'");
                    table.CheckConstraint("CK_ClassStatFactors_PowerPerIntellect", "\"PowerPerIntellect\" >= 0 AND \"PowerPerIntellect\" < 'Infinity'");
                });

            migrationBuilder.CreateTable(
                name: "CombatFormula",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ArmorBase = table.Column<float>(type: "real", nullable: false),
                    ArmorPerLevel = table.Column<float>(type: "real", nullable: false),
                    ArmorCap = table.Column<float>(type: "real", nullable: false),
                    CritMultiplier = table.Column<float>(type: "real", nullable: false),
                    BlockMultiplier = table.Column<float>(type: "real", nullable: false),
                    CritCap = table.Column<float>(type: "real", nullable: false),
                    DodgeCap = table.Column<float>(type: "real", nullable: false),
                    BlockCap = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CombatFormula", x => x.Id);
                    table.CheckConstraint("CK_CombatFormula_ArmorBase", "\"ArmorBase\" >= 0 AND \"ArmorBase\" < 'Infinity'");
                    table.CheckConstraint("CK_CombatFormula_ArmorCap", "\"ArmorCap\" >= 0 AND \"ArmorCap\" <= 1");
                    table.CheckConstraint("CK_CombatFormula_ArmorPerLevel", "\"ArmorPerLevel\" >= 0 AND \"ArmorPerLevel\" < 'Infinity'");
                    table.CheckConstraint("CK_CombatFormula_ArmorTermsPositive", "\"ArmorBase\" + \"ArmorPerLevel\" > 0");
                    table.CheckConstraint("CK_CombatFormula_BlockCap", "\"BlockCap\" >= 0 AND \"BlockCap\" <= 100");
                    table.CheckConstraint("CK_CombatFormula_BlockMultiplier", "\"BlockMultiplier\" >= 0 AND \"BlockMultiplier\" < 'Infinity'");
                    table.CheckConstraint("CK_CombatFormula_CritCap", "\"CritCap\" >= 0 AND \"CritCap\" <= 100");
                    table.CheckConstraint("CK_CombatFormula_CritMultiplier", "\"CritMultiplier\" >= 0 AND \"CritMultiplier\" < 'Infinity'");
                    table.CheckConstraint("CK_CombatFormula_DodgeCap", "\"DodgeCap\" >= 0 AND \"DodgeCap\" <= 100");
                    table.CheckConstraint("CK_CombatFormula_SingleRow", "\"Id\" = 1");
                });

            migrationBuilder.InsertData(
                table: "ClassStatFactors",
                columns: new[] { "Class", "AbilityPerIntellect", "AttackPerAgility", "AttackPerStrength", "BaseBlock", "BaseCrit", "BaseDodge", "FixedPower", "HpPerStamina", "PowerPerAgility", "PowerPerIntellect" },
                values: new object[,]
                {
                    { 1, 0.20000000000000001, 0.0, 2.0, 5f, 5f, 3.664f, 100L, 10L, 0.0, 0.0 },
                    { 2, 3.0, 0.0, 0.5, 0f, 1.85f, 3.25f, null, 5L, 0.0, 15.0 },
                    { 3, 0.5, 1.5, 0.5, 0f, 5f, 4.35f, null, 8L, 0.80000000000000004, 2.0 },
                    { 4, 2.0, 0.0, 0.5, 0f, 1.85f, 3.25f, null, 7L, 0.0, 12.0 }
                });

            migrationBuilder.InsertData(
                table: "CombatFormula",
                columns: new[] { "Id", "ArmorBase", "ArmorCap", "ArmorPerLevel", "BlockCap", "BlockMultiplier", "CritCap", "CritMultiplier", "DodgeCap" },
                values: new object[] { 1, 50f, 0.75f, 10f, 50f, 0.5f, 50f, 1.5f, 30f });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassStatFactors");

            migrationBuilder.DropTable(
                name: "CombatFormula");
        }
    }
}
