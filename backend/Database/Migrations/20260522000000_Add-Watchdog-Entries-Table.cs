using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchdogEntriesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchdogEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClickId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedTitle = table.Column<string>(type: "TEXT", nullable: false),
                    CandidateTitle = table.Column<string>(type: "TEXT", nullable: false),
                    IndexerName = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    RankIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Result = table.Column<int>(type: "INTEGER", nullable: false),
                    FailReason = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    IsWinner = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProviderHost = table.Column<string>(type: "TEXT", nullable: true),
                    QueueItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContentGroupKey = table.Column<string>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchdogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchdogEntries_AttemptedAt",
                table: "WatchdogEntries",
                column: "AttemptedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WatchdogEntries_QueueItemId",
                table: "WatchdogEntries",
                column: "QueueItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchdogEntries_ContentGroupKey",
                table: "WatchdogEntries",
                column: "ContentGroupKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WatchdogEntries");
        }
    }
}
