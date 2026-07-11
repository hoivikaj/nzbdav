using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantsColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentGroupKey",
                table: "QueueItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentGroupKey",
                table: "HistoryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastPlayedAt",
                table: "HistoryItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueItems_ContentGroupKey",
                table: "QueueItems",
                column: "ContentGroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryItems_ContentGroupKey_DownloadStatus",
                table: "HistoryItems",
                columns: new[] { "ContentGroupKey", "DownloadStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_HistoryItems_ContentGroupKey_DownloadStatus", table: "HistoryItems");
            migrationBuilder.DropIndex(name: "IX_QueueItems_ContentGroupKey", table: "QueueItems");
            migrationBuilder.DropColumn(name: "LastPlayedAt", table: "HistoryItems");
            migrationBuilder.DropColumn(name: "ContentGroupKey", table: "HistoryItems");
            migrationBuilder.DropColumn(name: "ContentGroupKey", table: "QueueItems");
        }
    }
}
