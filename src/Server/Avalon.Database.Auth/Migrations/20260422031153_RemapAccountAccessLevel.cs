using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class RemapAccountAccessLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                column: "AccessLevel",
                value: 7);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 1,
                column: "AccessLevelRequired",
                value: 4);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 2,
                column: "AccessLevelRequired",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 3,
                column: "AccessLevelRequired",
                value: 32);

            // Remap any existing non-seed rows bitwise from the old AccountAccessLevel
            // enum layout (Player=0, GameMaster=1, Administrator=2, Tournament=4, PTR=8)
            // to the new layout (Player=1, GameMaster=2, Admin=4, Console=8, Tournament=16, PTR=32).
            // Seed rows above have already been rewritten by UpdateData; running the SQL
            // against them is a no-op because their new values share no bits with the
            // old mapping (except Player=0 which is handled explicitly below).
            migrationBuilder.Sql("""
                UPDATE "Accounts" SET "AccessLevel" = (
                      (CASE WHEN "AccessLevel" = 0 THEN 1 ELSE 0 END)
                    | (CASE WHEN ("AccessLevel" & 1) = 1 THEN 2 ELSE 0 END)
                    | (CASE WHEN ("AccessLevel" & 2) = 2 THEN 4 ELSE 0 END)
                    | (CASE WHEN ("AccessLevel" & 4) = 4 THEN 16 ELSE 0 END)
                    | (CASE WHEN ("AccessLevel" & 8) = 8 THEN 32 ELSE 0 END)
                )
                WHERE "Id" <> 1;
            """);

            migrationBuilder.Sql("""
                UPDATE "Worlds" SET "AccessLevelRequired" = (
                      (CASE WHEN "AccessLevelRequired" = 0 THEN 1 ELSE 0 END)
                    | (CASE WHEN ("AccessLevelRequired" & 1) = 1 THEN 2 ELSE 0 END)
                    | (CASE WHEN ("AccessLevelRequired" & 2) = 2 THEN 4 ELSE 0 END)
                    | (CASE WHEN ("AccessLevelRequired" & 4) = 4 THEN 16 ELSE 0 END)
                    | (CASE WHEN ("AccessLevelRequired" & 8) = 8 THEN 32 ELSE 0 END)
                )
                WHERE "Id" NOT IN (1, 2, 3);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse-map any non-seed rows first, then restore seed rows.
            migrationBuilder.Sql("""
                UPDATE "Accounts" SET "AccessLevel" = (
                      (CASE WHEN ("AccessLevel" & 2)  = 2  THEN 1 ELSE 0 END)
                    | (CASE WHEN ("AccessLevel" & 4)  = 4  THEN 2 ELSE 0 END)
                    | (CASE WHEN ("AccessLevel" & 16) = 16 THEN 4 ELSE 0 END)
                    | (CASE WHEN ("AccessLevel" & 32) = 32 THEN 8 ELSE 0 END)
                )
                WHERE "Id" <> 1;
            """);

            migrationBuilder.Sql("""
                UPDATE "Worlds" SET "AccessLevelRequired" = (
                      (CASE WHEN ("AccessLevelRequired" & 2)  = 2  THEN 1 ELSE 0 END)
                    | (CASE WHEN ("AccessLevelRequired" & 4)  = 4  THEN 2 ELSE 0 END)
                    | (CASE WHEN ("AccessLevelRequired" & 16) = 16 THEN 4 ELSE 0 END)
                    | (CASE WHEN ("AccessLevelRequired" & 32) = 32 THEN 8 ELSE 0 END)
                )
                WHERE "Id" NOT IN (1, 2, 3);
            """);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                column: "AccessLevel",
                value: 3);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 1,
                column: "AccessLevelRequired",
                value: 2);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 2,
                column: "AccessLevelRequired",
                value: 0);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 3,
                column: "AccessLevelRequired",
                value: 8);
        }
    }
}
