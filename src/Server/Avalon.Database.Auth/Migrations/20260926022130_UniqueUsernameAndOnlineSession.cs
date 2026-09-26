using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class UniqueUsernameAndOnlineSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #487: registration checked the username, then inserted, so two registrations racing
            // on one name could both land. This migration never picks a winner: which account
            // keeps the name, and what happens to the other one's characters, is a decision for a
            // person. It stops, naming the accounts, when
            //   1. two accounts share a username once it is normalised (trimmed, upper-cased: the
            //      form registration stores and login looks up), or
            //   2. an account's username is not stored normalised, since then an index on the
            //      column would not be an index on the normalised name.
            // Fix the rows by hand, then run the migration again. Nothing is changed or deleted.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    duplicates text;
                    unnormalised text;
                BEGIN
                    SELECT string_agg(format('%s (account ids %s)', name, ids), '; ')
                      INTO duplicates
                      FROM (SELECT upper(btrim("Username")) AS name,
                                   string_agg("Id"::text, ', ' ORDER BY "Id") AS ids
                              FROM "Accounts"
                             GROUP BY upper(btrim("Username"))
                            HAVING count(*) > 1) AS d;

                    IF duplicates IS NOT NULL THEN
                        RAISE EXCEPTION 'Cannot add the unique index on "Accounts"."Username": these usernames are held by more than one account: %. Resolve them by hand; this migration deletes nothing.', duplicates;
                    END IF;

                    SELECT string_agg(format('%s (account id %s)', "Username", "Id"), '; ' ORDER BY "Id")
                      INTO unnormalised
                      FROM "Accounts"
                     WHERE "Username" <> upper(btrim("Username"));

                    IF unnormalised IS NOT NULL THEN
                        RAISE EXCEPTION 'Cannot add the unique index on "Accounts"."Username": these usernames are not stored trimmed and upper-cased: %. Normalise them by hand; this migration changes nothing.', unnormalised;
                    END IF;
                END
                $$;
                """);

            // #487: which auth-server connection set Online, so a stale close cannot clear the
            // flag a newer session set. Rows online now have none; the auth server clears every
            // Online flag when it starts, which it must do to run this code, so none is left.
            migrationBuilder.AddColumn<Guid>(
                name: "OnlineSessionId",
                table: "Accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                column: "OnlineSessionId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Username",
                table: "Accounts",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_Username",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "OnlineSessionId",
                table: "Accounts");
        }
    }
}
