using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCapstonesAndKeystones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF also emitted a no-op update per existing node (Keystone null on a column added null), removed by hand.
            migrationBuilder.AddColumn<byte>(
                name: "Keystone",
                table: "TalentNodes",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TalentAbilityModifiers",
                columns: table => new
                {
                    NodeId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    AbilityId = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    Value2 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalentAbilityModifiers", x => new { x.NodeId, x.Kind });
                    table.CheckConstraint("CK_TalentAbilityModifiers_Kind", "\"Kind\" BETWEEN 1 AND 8");
                    table.CheckConstraint("CK_TalentAbilityModifiers_Value", "\"Value\" > 0");
                    table.CheckConstraint("CK_TalentAbilityModifiers_Value2", "(\"Kind\" = 2) = (\"Value2\" IS NOT NULL) AND (\"Value2\" IS NULL OR \"Value2\" > 0)");
                    table.ForeignKey(
                        name: "FK_TalentAbilityModifiers_AbilityTemplates_AbilityId",
                        column: x => x.AbilityId,
                        principalTable: "AbilityTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentAbilityModifiers_TalentNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "TalentNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "TalentForkGroups",
                column: "Id",
                values: new object[]
                {
                    1,
                    2,
                    3,
                    4,
                    5,
                    6,
                    7,
                    8,
                    9
                });

            migrationBuilder.InsertData(
                table: "TalentNodes",
                columns: new[] { "Id", "Angle", "ForkGroupId", "Keystone", "Kind", "Name", "Radius" },
                values: new object[,]
                {
                    { 308, -50f, null, null, (byte)5, "Haemorrhage", 320f },
                    { 311, -118f, null, null, (byte)5, "Twin Axes", 400f },
                    { 314, -40f, null, null, (byte)5, "Twin Arrows", 320f },
                    { 315, 20f, null, null, (byte)5, "Double Tap", 880f },
                    { 320, 50f, null, null, (byte)5, "Frost Field", 320f },
                    { 321, 110f, null, null, (byte)5, "Conflagration", 880f },
                    { 324, 160f, null, null, (byte)5, "Thunderclap", 880f },
                    { 327, -140f, null, null, (byte)5, "Enduring Renewal", 320f },
                    { 332, -45f, null, (byte)1, (byte)6, "Blood Price", 120f },
                    { 333, 45f, null, null, (byte)6, "Glass Edge", 120f },
                    { 334, 135f, null, (byte)2, (byte)6, "Steady Hand", 120f },
                    { 335, -135f, null, (byte)3, (byte)6, "Unbroken", 120f }
                });

            migrationBuilder.InsertData(
                table: "TalentAbilityModifiers",
                columns: new[] { "Kind", "NodeId", "AbilityId", "Value", "Value2" },
                values: new object[,]
                {
                    { (byte)6, 308, 203L, 2, null },
                    { (byte)1, 311, 202L, 1, null },
                    { (byte)1, 314, 221L, 1, null },
                    { (byte)8, 315, 223L, 50, null },
                    { (byte)3, 320, 214L, 40, null },
                    { (byte)3, 321, 211L, 40, null },
                    { (byte)8, 324, 231L, 50, null },
                    { (byte)7, 327, 233L, 50, null }
                });

            migrationBuilder.InsertData(
                table: "TalentEdges",
                columns: new[] { "NodeA", "NodeB" },
                values: new object[,]
                {
                    { 31, 308 },
                    { 73, 311 },
                    { 104, 314 },
                    { 129, 315 },
                    { 177, 320 },
                    { 202, 321 },
                    { 245, 324 },
                    { 275, 327 },
                    { 302, 332 },
                    { 302, 335 },
                    { 303, 332 },
                    { 303, 333 },
                    { 304, 333 },
                    { 304, 334 },
                    { 305, 334 },
                    { 305, 335 }
                });

            migrationBuilder.InsertData(
                table: "TalentNodeStats",
                columns: new[] { "Kind", "NodeId", "Stat", "Value", "ValueMax" },
                values: new object[,]
                {
                    { (byte)2, 333, (short)19, 30, null },
                    { (byte)2, 333, (short)53, -25, null },
                    { (byte)2, 334, (short)19, 20, null },
                    { (byte)2, 334, (short)31, 20, null }
                });

            migrationBuilder.InsertData(
                table: "TalentNodes",
                columns: new[] { "Id", "Angle", "ForkGroupId", "Keystone", "Kind", "Name", "Radius" },
                values: new object[,]
                {
                    { 306, -72f, 1, null, (byte)5, "Wide Arc", 880f },
                    { 307, -68f, 1, null, (byte)5, "Long Reach", 880f },
                    { 309, -132f, 2, null, (byte)5, "Earthshaker", 320f },
                    { 310, -128f, 2, null, (byte)5, "Aftershock", 320f },
                    { 312, -22f, 3, null, (byte)5, "Broad Volley", 880f },
                    { 313, -18f, 3, null, (byte)5, "Long Volley", 880f },
                    { 316, 38f, 4, null, (byte)5, "Blight Burst", 320f },
                    { 317, 42f, 4, null, (byte)5, "Twin Binding", 320f },
                    { 318, 68f, 5, null, (byte)5, "Glacial Reach", 880f },
                    { 319, 72f, 5, null, (byte)5, "Wide Fan", 880f },
                    { 322, 128f, 6, null, (byte)5, "Wildfire", 320f },
                    { 323, 132f, 6, null, (byte)5, "Firestorm", 320f },
                    { 325, -162f, 7, null, (byte)5, "Sanctuary", 880f },
                    { 326, -158f, 7, null, (byte)5, "Twice Blessed", 880f },
                    { 328, 150f, 8, null, (byte)5, "Bastion", 400f },
                    { 329, 154f, 8, null, (byte)5, "Steadfast", 400f },
                    { 330, 43f, 9, null, (byte)5, "Shatterbolt", 840f },
                    { 331, 47f, 9, null, (byte)5, "Rimeburst", 840f }
                });

            migrationBuilder.InsertData(
                table: "TalentAbilityModifiers",
                columns: new[] { "Kind", "NodeId", "AbilityId", "Value", "Value2" },
                values: new object[,]
                {
                    { (byte)5, 306, 200L, 50, null },
                    { (byte)4, 307, 200L, 40, null },
                    { (byte)3, 309, 201L, 40, null },
                    { (byte)8, 310, 201L, 50, null },
                    { (byte)5, 312, 222L, 40, null },
                    { (byte)4, 313, 222L, 40, null },
                    { (byte)2, 316, 224L, 25, 50 },
                    { (byte)1, 317, 224L, 1, null },
                    { (byte)4, 318, 212L, 40, null },
                    { (byte)5, 319, 212L, 50, null },
                    { (byte)7, 322, 213L, 50, null },
                    { (byte)3, 323, 213L, 50, null },
                    { (byte)3, 325, 232L, 40, null },
                    { (byte)8, 326, 232L, 50, null },
                    { (byte)3, 328, 234L, 50, null },
                    { (byte)7, 329, 234L, 50, null },
                    { (byte)1, 330, 320L, 2, null },
                    { (byte)2, 331, 320L, 30, 60 }
                });

            migrationBuilder.InsertData(
                table: "TalentEdges",
                columns: new[] { "NodeA", "NodeB" },
                values: new object[,]
                {
                    { 26, 306 },
                    { 26, 307 },
                    { 56, 309 },
                    { 56, 310 },
                    { 99, 312 },
                    { 99, 313 },
                    { 134, 316 },
                    { 134, 317 },
                    { 172, 318 },
                    { 172, 319 },
                    { 207, 322 },
                    { 207, 323 },
                    { 270, 325 },
                    { 270, 326 },
                    { 292, 328 },
                    { 292, 329 },
                    { 301, 330 },
                    { 301, 331 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TalentNodes_Keystone",
                table: "TalentNodes",
                sql: "\"Keystone\" IS NULL OR (\"Kind\" = 6 AND \"Keystone\" BETWEEN 1 AND 3)");

            migrationBuilder.CreateIndex(
                name: "IX_TalentAbilityModifiers_AbilityId",
                table: "TalentAbilityModifiers",
                column: "AbilityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TalentAbilityModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TalentNodes_Keystone",
                table: "TalentNodes");

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 26, 306 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 26, 307 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 31, 308 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 56, 309 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 56, 310 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 73, 311 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 99, 312 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 99, 313 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 104, 314 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 129, 315 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 134, 316 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 134, 317 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 172, 318 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 172, 319 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 177, 320 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 202, 321 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 207, 322 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 207, 323 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 245, 324 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 270, 325 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 270, 326 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 275, 327 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 292, 328 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 292, 329 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 301, 330 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 301, 331 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 302, 332 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 302, 335 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 303, 332 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 303, 333 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 304, 333 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 304, 334 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 305, 334 });

            migrationBuilder.DeleteData(
                table: "TalentEdges",
                keyColumns: new[] { "NodeA", "NodeB" },
                keyValues: new object[] { 305, 335 });

            migrationBuilder.DeleteData(
                table: "TalentNodeStats",
                keyColumns: new[] { "Kind", "NodeId", "Stat" },
                keyValues: new object[] { (byte)2, 333, (short)19 });

            migrationBuilder.DeleteData(
                table: "TalentNodeStats",
                keyColumns: new[] { "Kind", "NodeId", "Stat" },
                keyValues: new object[] { (byte)2, 333, (short)53 });

            migrationBuilder.DeleteData(
                table: "TalentNodeStats",
                keyColumns: new[] { "Kind", "NodeId", "Stat" },
                keyValues: new object[] { (byte)2, 334, (short)19 });

            migrationBuilder.DeleteData(
                table: "TalentNodeStats",
                keyColumns: new[] { "Kind", "NodeId", "Stat" },
                keyValues: new object[] { (byte)2, 334, (short)31 });

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 306);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 307);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 308);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 309);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 310);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 311);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 312);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 313);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 314);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 315);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 316);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 317);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 318);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 319);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 320);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 321);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 322);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 323);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 324);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 325);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 326);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 327);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 328);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 329);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 330);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 331);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 332);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 333);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 334);

            migrationBuilder.DeleteData(
                table: "TalentNodes",
                keyColumn: "Id",
                keyValue: 335);

            migrationBuilder.DeleteData(
                table: "TalentForkGroups",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "TalentForkGroups",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "TalentForkGroups",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "TalentForkGroups",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "TalentForkGroups",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "TalentForkGroups",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "TalentForkGroups",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "TalentForkGroups",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "TalentForkGroups",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DropColumn(
                name: "Keystone",
                table: "TalentNodes");
        }
    }
}
