using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Database;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    [DbContext(typeof(DavDatabaseContext))]
    [Migration("20260604120000_Add-UpdatedAtUnix-Index-To-WantedItems")]
    public partial class AddUpdatedAtUnixIndexToWantedItems : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WantedItems_UpdatedAtUnix",
                table: "WantedItems",
                column: "UpdatedAtUnix");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WantedItems_UpdatedAtUnix",
                table: "WantedItems");
        }
    }
}
