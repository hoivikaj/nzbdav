using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Database;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(DavDatabaseContext))]
    [Migration("20260602000000_Add-Watchtower-Tables")]
    public partial class AddWatchtowerTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Cap = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUnix = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSyncedAtUnix = table.Column<long>(type: "INTEGER", nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WantedItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    ContentId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Provenance = table.Column<string>(type: "TEXT", nullable: false),
                    Shortlist = table.Column<string>(type: "TEXT", nullable: false),
                    WinnerNzb = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ResponderHost = table.Column<string>(type: "TEXT", nullable: true),
                    FailReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUnix = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUnix = table.Column<long>(type: "INTEGER", nullable: false),
                    LastResolvedAtUnix = table.Column<long>(type: "INTEGER", nullable: true),
                    LastVerifiedAtUnix = table.Column<long>(type: "INTEGER", nullable: true),
                    NextCheckAtUnix = table.Column<long>(type: "INTEGER", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WantedItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WantedItems_Key",
                table: "WantedItems",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WantedItems_NextCheckAtUnix",
                table: "WantedItems",
                column: "NextCheckAtUnix");

            migrationBuilder.CreateIndex(
                name: "IX_WantedItems_State",
                table: "WantedItems",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WantedItems");
            migrationBuilder.DropTable(name: "ListSources");
        }
    }
}
