using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerApiHitsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IndexerApiHits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndexerName = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    AccessedAt = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexerApiHits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndexerApiHits_IndexerName_Type_AccessedAt",
                table: "IndexerApiHits",
                columns: new[] { "IndexerName", "Type", "AccessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IndexerApiHits_AccessedAt",
                table: "IndexerApiHits",
                column: "AccessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IndexerApiHits");
        }
    }
}
