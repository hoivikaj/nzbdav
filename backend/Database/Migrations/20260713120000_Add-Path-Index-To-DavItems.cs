using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Database;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <summary>
    /// Adds a unique index on <c>DavItems.Path</c> so WebDAV resolution can look up a
    /// persisted item in a single query instead of walking one segment at a time.
    /// Rebuilds Path values first and renames any leftover duplicates so the unique
    /// index can be created safely on existing databases.
    /// </summary>
    [DbContext(typeof(DavDatabaseContext))]
    [Migration("20260713120000_Add-Path-Index-To-DavItems")]
    public partial class AddPathIndexToDavItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Rebuild Path from the parent chain so denormalized values match Name.
            AddPathToDavItem.BuildFullPath(migrationBuilder);

            // 2. Repair any leftover duplicate Paths (should be rare after rebuild given
            //    the unique (ParentId, Name) index). Rename later duplicates so the
            //    unique Path index can be created without failing the migration.
            migrationBuilder.Sql("""
                UPDATE DavItems
                SET Name = Name || ' (' || substr(Id, 1, 5) || ')'
                WHERE Id IN (
                    SELECT Id FROM (
                        SELECT Id,
                               ROW_NUMBER() OVER (PARTITION BY Path ORDER BY CreatedAt, Id) AS rn
                        FROM DavItems
                    )
                    WHERE rn > 1
                );
                """);

            // 3. Rebuild again so renamed Names propagate into Path (and descendants).
            AddPathToDavItem.BuildFullPath(migrationBuilder);

            migrationBuilder.CreateIndex(
                name: "IX_DavItems_Path",
                table: "DavItems",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DavItems_Path",
                table: "DavItems");
        }
    }
}
