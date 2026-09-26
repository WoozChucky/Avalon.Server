using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class UniqueNormalisedEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #503: registration and the email change stored the email as sent, and the lookup was
            // exact, so A@x.com and a@x.com could belong to two accounts. This migration never picks
            // a winner: which account keeps the address is a decision for a person. It stops,
            // naming the accounts, when
            //   1. two accounts share an email once it is normalised (trimmed, lower-cased: the form
            //      the API now stores and looks up), or
            //   2. an account's email is not stored normalised, since then an index on the column
            //      would not be an index on the normalised address.
            // Fix the rows by hand, then run the migration again. Nothing is changed or deleted.
            // The check constraint added at the end would refuse such rows too, with Postgres's own
            // error; this names them first.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    duplicates text;
                    unnormalised text;
                BEGIN
                    SELECT string_agg(format('%s (account ids %s)', address, ids), '; ')
                      INTO duplicates
                      FROM (SELECT lower(btrim("Email")) AS address,
                                   string_agg("Id"::text, ', ' ORDER BY "Id") AS ids
                              FROM "Accounts"
                             GROUP BY lower(btrim("Email"))
                            HAVING count(*) > 1) AS d;

                    IF duplicates IS NOT NULL THEN
                        RAISE EXCEPTION 'Cannot add the unique index on "Accounts"."Email": these emails are held by more than one account: %. Resolve them by hand; this migration deletes nothing.', duplicates;
                    END IF;

                    SELECT string_agg(format('%s (account id %s)', "Email", "Id"), '; ' ORDER BY "Id")
                      INTO unnormalised
                      FROM "Accounts"
                     WHERE "Email" <> lower(btrim("Email"));

                    IF unnormalised IS NOT NULL THEN
                        RAISE EXCEPTION 'Cannot add the unique index on "Accounts"."Email": these emails are not stored trimmed and lower-cased: %. Normalise them by hand; this migration changes nothing.', unnormalised;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Email",
                table: "Accounts",
                column: "Email",
                unique: true);

            // Every writer must store the normalised form, or the index above stops being on the
            // normalised address. The database holds them to it from here on.
            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_Email_Normalised",
                table: "Accounts",
                sql: "\"Email\" = lower(trim(\"Email\"))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_Email_Normalised",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_Email",
                table: "Accounts");
        }
    }
}
