using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterAuras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterAuras",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    AuraId = table.Column<long>(type: "bigint", nullable: false),
                    CasterGuid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    SourceAbilityId = table.Column<long>(type: "bigint", nullable: true),
                    Stacks = table.Column<int>(type: "integer", nullable: false),
                    RemainingMs = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    TicksLeft = table.Column<int>(type: "integer", nullable: false),
                    TickAmount = table.Column<float>(type: "real", nullable: false),
                    CritPct = table.Column<float>(type: "real", nullable: false),
                    CasterLevel = table.Column<int>(type: "integer", nullable: false),
                    PeriodicCarry = table.Column<double>(type: "double precision", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterAuras", x => new { x.CharacterId, x.Slot });
                    table.CheckConstraint("CK_CharacterAuras_Stacks", "\"Stacks\" >= 1");
                    table.CheckConstraint("CK_CharacterAuras_TicksLeft", "\"TicksLeft\" >= 0");
                    table.ForeignKey(
                        name: "FK_CharacterAuras_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterAuras");
        }
    }
}
