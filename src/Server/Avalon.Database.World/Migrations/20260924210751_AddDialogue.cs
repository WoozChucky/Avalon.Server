using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddDialogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterClassNames",
                columns: table => new
                {
                    Class = table.Column<string>(type: "text", nullable: false),
                    TextId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterClassNames", x => x.Class);
                });

            migrationBuilder.CreateTable(
                name: "DialogueNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatureTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    IsRoot = table.Column<bool>(type: "boolean", nullable: false),
                    TextId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DialogueNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DialogueOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NodeId = table.Column<int>(type: "integer", nullable: false),
                    TextId = table.Column<int>(type: "integer", nullable: false),
                    NextNodeId = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DialogueOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalizedTextLocales",
                columns: table => new
                {
                    TextId = table.Column<int>(type: "integer", nullable: false),
                    Locale = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalizedTextLocales", x => new { x.TextId, x.Locale });
                });

            migrationBuilder.CreateTable(
                name: "LocalizedTexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalizedTexts", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "CharacterClassNames",
                columns: new[] { "Class", "TextId" },
                values: new object[,]
                {
                    { "Healer", 14 },
                    { "Hunter", 13 },
                    { "Warrior", 11 },
                    { "Wizard", 12 }
                });

            migrationBuilder.InsertData(
                table: "DialogueNodes",
                columns: new[] { "Id", "CreatureTemplateId", "IsRoot", "TextId" },
                values: new object[,]
                {
                    { 1, 1m, true, 1 },
                    { 2, 1m, false, 2 },
                    { 3, 1m, false, 3 },
                    { 4, 2m, true, 4 },
                    { 5, 2m, false, 5 },
                    { 6, 3m, true, 6 }
                });

            migrationBuilder.InsertData(
                table: "DialogueOptions",
                columns: new[] { "Id", "NextNodeId", "NodeId", "SortOrder", "TextId" },
                values: new object[,]
                {
                    { 1, 2, 1, (short)0, 7 },
                    { 2, null, 1, (short)1, 10 },
                    { 3, 3, 2, (short)0, 8 },
                    { 4, null, 2, (short)1, 10 },
                    { 5, null, 3, (short)0, 10 },
                    { 6, 5, 4, (short)0, 9 },
                    { 7, null, 4, (short)1, 10 },
                    { 8, null, 5, (short)0, 10 },
                    { 9, null, 6, (short)0, 10 }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTextLocales",
                columns: new[] { "Locale", "TextId", "Text" },
                values: new object[,]
                {
                    { "ptPT", 1, "A mata escurece a cada estação, {name}. Já há estações em que nunca chega a clarear." },
                    { "ptPT", 2, "Algo se enraizou no coração dela. Os bichos sentem-no antes de nós — mudam, e depois não voltam a ser o que eram." },
                    { "ptPT", 3, "Alguns regressam. Mas não tudo o que regressa é quem partiu." },
                    { "ptPT", 4, "O aço aguenta. A madeira apodrece. Lembra-te de qual dos dois levas contigo quando caminhares debaixo daquelas árvores." },
                    { "ptPT", 5, "Uma vez, e voltei pela bigorna e não pela paisagem. {g:Um|Uma} {class} talvez se saia melhor do que um ferreiro se saiu." },
                    { "ptPT", 6, "Sê bem-{g:vindo|vinda}, {name}. O quarto é lá em cima, o guisado está ao lume, e não faço perguntas sobre o estado das tuas botas." },
                    { "ptPT", 7, "O que mudou?" },
                    { "ptPT", 8, "E os que entram?" },
                    { "ptPT", 9, "Já lá entraste?" },
                    { "ptPT", 10, "Adeus." },
                    { "ptPT", 11, "Guerreir{g:o|a}" },
                    { "ptPT", 12, "Mag{g:o|a}" },
                    { "ptPT", 13, "Caçador{g:|a}" },
                    { "ptPT", 14, "Curandeir{g:o|a}" }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTexts",
                columns: new[] { "Id", "Text" },
                values: new object[,]
                {
                    { 1, "The wold grows darker each season, {name}. There are seasons now where it does not lighten at all." },
                    { 2, "Something took root at its heart. The beasts feel it before we do — they change, and then they do not change back." },
                    { 3, "Some return. Not all of what returns is who left." },
                    { 4, "Steel holds. Wood rots. Remember which one you are carrying when you walk under those trees." },
                    { 5, "Once, and I came back for the anvil rather than the view. A {class} might fare better than a smith did." },
                    { 6, "Room's upstairs, {name}, stew's on, and I ask no questions about the state of your boots." },
                    { 7, "What changed?" },
                    { 8, "And the ones who go in?" },
                    { 9, "Have you been in?" },
                    { 10, "Farewell." },
                    { 11, "Warrior" },
                    { 12, "Wizard" },
                    { 13, "Hunter" },
                    { 14, "Healer" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DialogueNodes_CreatureTemplateId",
                table: "DialogueNodes",
                column: "CreatureTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_DialogueOptions_NodeId",
                table: "DialogueOptions",
                column: "NodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterClassNames");

            migrationBuilder.DropTable(
                name: "DialogueNodes");

            migrationBuilder.DropTable(
                name: "DialogueOptions");

            migrationBuilder.DropTable(
                name: "LocalizedTextLocales");

            migrationBuilder.DropTable(
                name: "LocalizedTexts");
        }
    }
}
