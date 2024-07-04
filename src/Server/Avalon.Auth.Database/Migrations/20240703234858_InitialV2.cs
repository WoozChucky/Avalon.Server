using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Auth.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "JoinDate", "LastLogin", "Salt", "Username", "Verifier" },
                values: new object[] { new DateTime(2024, 7, 3, 23, 48, 58, 53, DateTimeKind.Utc).AddTicks(5864), new DateTime(2024, 7, 3, 23, 48, 58, 53, DateTimeKind.Utc).AddTicks(5868), new byte[] { 36, 50, 97, 36, 49, 49, 36, 113, 75, 77, 69, 57, 74, 120, 90, 105, 56, 85, 47, 68, 122, 70, 79, 80, 102, 65, 76, 116, 46 }, "ADMIN", new byte[] { 36, 50, 97, 36, 49, 49, 36, 113, 75, 77, 69, 57, 74, 120, 90, 105, 56, 85, 47, 68, 122, 70, 79, 80, 102, 65, 76, 116, 46, 107, 50, 122, 99, 55, 48, 122, 85, 82, 75, 121, 112, 83, 48, 66, 121, 80, 88, 89, 57, 103, 70, 109, 49, 84, 69, 102, 84, 120, 78, 113 } });

            migrationBuilder.InsertData(
                table: "Worlds",
                columns: new[] { "Id", "AccessLevelRequired", "CreatedAt", "Host", "MinVersion", "Name", "Port", "Status", "Type", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 1, (short)2, new DateTime(2024, 7, 3, 23, 48, 58, 53, DateTimeKind.Utc).AddTicks(6386), "127.0.0.1", "0.0.1", "Development", 21001, (short)1, (short)0, new DateTime(2024, 7, 3, 23, 48, 58, 53, DateTimeKind.Utc).AddTicks(6386), "0.0.1" },
                    { 2, (short)0, new DateTime(2024, 7, 3, 23, 48, 58, 53, DateTimeKind.Utc).AddTicks(6388), "asthoria.avalon.monster", "0.0.1", "Asthoria", 21001, (short)0, (short)0, new DateTime(2024, 7, 3, 23, 48, 58, 53, DateTimeKind.Utc).AddTicks(6389), "0.0.1" },
                    { 3, (short)8, new DateTime(2024, 7, 3, 23, 48, 58, 53, DateTimeKind.Utc).AddTicks(6391), "ptr.avalon.monster", "0.0.1", "Public Test Realm", 21001, (short)0, (short)0, new DateTime(2024, 7, 3, 23, 48, 58, 53, DateTimeKind.Utc).AddTicks(6391), "0.0.1" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "JoinDate", "LastLogin", "Salt", "Username", "Verifier" },
                values: new object[] { new DateTime(2024, 7, 3, 21, 50, 23, 607, DateTimeKind.Utc).AddTicks(5516), new DateTime(2024, 7, 3, 21, 50, 23, 607, DateTimeKind.Utc).AddTicks(5520), new byte[] { 36, 50, 97, 36, 49, 49, 36, 106, 76, 81, 118, 71, 81, 53, 118, 75, 117, 105, 109, 107, 67, 110, 116, 84, 108, 107, 48, 108, 46 }, "admin", new byte[] { 36, 50, 97, 36, 49, 49, 36, 106, 76, 81, 118, 71, 81, 53, 118, 75, 117, 105, 109, 107, 67, 110, 116, 84, 108, 107, 48, 108, 46, 89, 106, 119, 121, 66, 47, 101, 115, 79, 113, 114, 57, 49, 74, 108, 114, 113, 118, 75, 118, 117, 110, 80, 65, 70, 71, 68, 97, 112, 51, 79 } });
        }
    }
}
