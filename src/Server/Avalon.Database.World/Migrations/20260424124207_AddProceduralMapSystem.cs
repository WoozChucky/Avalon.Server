using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddProceduralMapSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReturnMapId",
                table: "MapTemplates",
                newName: "LogoutMapId");

            migrationBuilder.CreateTable(
                name: "ChunkPools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkPools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChunkTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AssetKey = table.Column<string>(type: "text", nullable: false),
                    GeometryFile = table.Column<string>(type: "text", nullable: false),
                    CellFootprintX = table.Column<byte>(type: "smallint", nullable: false),
                    CellFootprintZ = table.Column<byte>(type: "smallint", nullable: false),
                    CellSize = table.Column<float>(type: "real", nullable: false),
                    Exits = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProceduralMapConfigs",
                columns: table => new
                {
                    MapTemplateId = table.Column<int>(type: "integer", nullable: false),
                    ChunkPoolId = table.Column<int>(type: "integer", nullable: false),
                    SpawnTableId = table.Column<int>(type: "integer", nullable: false),
                    MainPathMin = table.Column<int>(type: "integer", nullable: false),
                    MainPathMax = table.Column<int>(type: "integer", nullable: false),
                    BranchChance = table.Column<float>(type: "real", nullable: false),
                    BranchMaxDepth = table.Column<byte>(type: "smallint", nullable: false),
                    HasBoss = table.Column<bool>(type: "boolean", nullable: false),
                    BackPortalTargetMapId = table.Column<int>(type: "integer", nullable: false),
                    ForwardPortalTargetMapId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProceduralMapConfigs", x => x.MapTemplateId);
                });

            migrationBuilder.CreateTable(
                name: "SpawnTables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpawnTables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChunkPoolMembership",
                columns: table => new
                {
                    ChunkPoolId = table.Column<int>(type: "integer", nullable: false),
                    ChunkTemplateId = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkPoolMembership", x => new { x.ChunkPoolId, x.ChunkTemplateId });
                    table.ForeignKey(
                        name: "FK_ChunkPoolMembership_ChunkPools_ChunkPoolId",
                        column: x => x.ChunkPoolId,
                        principalTable: "ChunkPools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChunkPoolMembership_ChunkTemplates_ChunkTemplateId",
                        column: x => x.ChunkTemplateId,
                        principalTable: "ChunkTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChunkPortalSlot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Role = table.Column<byte>(type: "smallint", nullable: false),
                    LocalX = table.Column<float>(type: "real", nullable: false),
                    LocalY = table.Column<float>(type: "real", nullable: false),
                    LocalZ = table.Column<float>(type: "real", nullable: false),
                    ChunkTemplateId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkPortalSlot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChunkPortalSlot_ChunkTemplates_ChunkTemplateId",
                        column: x => x.ChunkTemplateId,
                        principalTable: "ChunkTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChunkSpawnSlot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    LocalX = table.Column<float>(type: "real", nullable: false),
                    LocalY = table.Column<float>(type: "real", nullable: false),
                    LocalZ = table.Column<float>(type: "real", nullable: false),
                    ChunkTemplateId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkSpawnSlot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChunkSpawnSlot_ChunkTemplates_ChunkTemplateId",
                        column: x => x.ChunkTemplateId,
                        principalTable: "ChunkTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpawnTableEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpawnTableId = table.Column<int>(type: "integer", nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    CreatureId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Weight = table.Column<float>(type: "real", nullable: false),
                    MinCount = table.Column<byte>(type: "smallint", nullable: false),
                    MaxCount = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpawnTableEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpawnTableEntry_SpawnTables_SpawnTableId",
                        column: x => x.SpawnTableId,
                        principalTable: "SpawnTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkPoolMembership_ChunkTemplateId",
                table: "ChunkPoolMembership",
                column: "ChunkTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ChunkPortalSlot_ChunkTemplateId",
                table: "ChunkPortalSlot",
                column: "ChunkTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ChunkSpawnSlot_ChunkTemplateId",
                table: "ChunkSpawnSlot",
                column: "ChunkTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_SpawnTableEntry_SpawnTableId",
                table: "SpawnTableEntry",
                column: "SpawnTableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChunkPoolMembership");

            migrationBuilder.DropTable(
                name: "ChunkPortalSlot");

            migrationBuilder.DropTable(
                name: "ChunkSpawnSlot");

            migrationBuilder.DropTable(
                name: "ProceduralMapConfigs");

            migrationBuilder.DropTable(
                name: "SpawnTableEntry");

            migrationBuilder.DropTable(
                name: "ChunkPools");

            migrationBuilder.DropTable(
                name: "ChunkTemplates");

            migrationBuilder.DropTable(
                name: "SpawnTables");

            migrationBuilder.RenameColumn(
                name: "LogoutMapId",
                table: "MapTemplates",
                newName: "ReturnMapId");
        }
    }
}
