using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class UniqueMfaSetupPerAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #470: an account could hold more than one MFA row. Keep one per account and delete
            // the rest, so the unique index below can be built. The row kept is, in order:
            //   1. a Confirmed row (Status 0) over any other, so an enrolled account stays enrolled;
            //   2. among those, the latest ConfirmedAt: the secret and codes the user set up last;
            //   3. then the latest CreatedAt (the table has no updated-at column; a setup that is
            //      replaced rewrites CreatedAt, so it is the most recent write for pending rows);
            //   4. then the highest Id, only so that the choice is deterministic.
            // The deleted rows are not restored by Down.
            migrationBuilder.Sql(
                """
                DELETE FROM "MfaSetups" AS m
                USING (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "AccountId"
                               ORDER BY ("Status" = 0) DESC, "ConfirmedAt" DESC, "CreatedAt" DESC, "Id" DESC
                           ) AS rank
                    FROM "MfaSetups"
                ) AS ranked
                WHERE m."Id" = ranked."Id" AND ranked.rank > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_MfaSetups_AccountId",
                table: "MfaSetups");

            migrationBuilder.CreateIndex(
                name: "IX_MfaSetups_AccountId",
                table: "MfaSetups",
                column: "AccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MfaSetups_AccountId",
                table: "MfaSetups");

            migrationBuilder.CreateIndex(
                name: "IX_MfaSetups_AccountId",
                table: "MfaSetups",
                column: "AccountId");
        }
    }
}
