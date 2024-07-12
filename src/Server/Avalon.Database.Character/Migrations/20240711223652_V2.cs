using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class V2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<ushort>(
                name: "Map",
                table: "Characters",
                type: "smallint unsigned",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<float>(
                name: "Rotation",
                table: "Characters",
                type: "float",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rotation",
                table: "Characters");

            migrationBuilder.AlterColumn<int>(
                name: "Map",
                table: "Characters",
                type: "int",
                nullable: false,
                oldClrType: typeof(ushort),
                oldType: "smallint unsigned");
        }
    }
}
