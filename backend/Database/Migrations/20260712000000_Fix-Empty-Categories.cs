using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Database;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <summary>
    /// Repairs legacy empty/whitespace Category values and empty-named /content folders
    /// that break WebDAV paths and explore URLs. Collisions are resolved by renaming
    /// rather than UPDATE OR IGNORE / cascade delete so migration never bricks installs
    /// that need this repair (entrypoint exits on --db-migration failure).
    /// </summary>
    [DbContext(typeof(DavDatabaseContext))]
    [Migration("20260712000000_Fix-Empty-Categories")]
    public partial class FixEmptyCategories : Migration
    {
        private const string ContentFolderId = "00000000-0000-0000-0000-000000000002";

        /// <summary>
        /// Configured manual category, falling back to uncategorized.
        /// Empty/whitespace ConfigValue is treated as unset.
        /// </summary>
        private const string TargetCategorySql = """
            COALESCE(
                NULLIF(TRIM((SELECT ConfigValue FROM ConfigItems WHERE ConfigName = 'api.manual-category')), ''),
                'uncategorized'
            )
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1–3. Rename QueueItems that would collide on (Category, FileName), then
            // rewrite empty/whitespace categories on HistoryItems and QueueItems.
            migrationBuilder.Sql($"""
                UPDATE QueueItems
                SET FileName = FileName || ' (' || substr(Id, 1, 5) || ')'
                WHERE TRIM(Category) = ''
                  AND EXISTS (
                      SELECT 1
                      FROM QueueItems AS existing
                      WHERE existing.Category = ({TargetCategorySql})
                        AND existing.FileName = QueueItems.FileName
                  );
                """);

            migrationBuilder.Sql($"""
                UPDATE HistoryItems
                SET Category = ({TargetCategorySql})
                WHERE TRIM(Category) = '';
                """);

            migrationBuilder.Sql($"""
                UPDATE QueueItems
                SET Category = ({TargetCategorySql})
                WHERE TRIM(Category) = '';
                """);

            // 4. Ensure the target category folder exists under /content.
            migrationBuilder.Sql($"""
                WITH target AS (
                    SELECT ({TargetCategorySql}) AS Name
                ),
                new_id AS (
                    SELECT lower(
                        hex(randomblob(4)) || '-' ||
                        hex(randomblob(2)) || '-' ||
                        '4' || substr(hex(randomblob(2)), 2) || '-' ||
                        substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) || '-' ||
                        hex(randomblob(6))
                    ) AS Id
                )
                INSERT INTO DavItems (Id, IdPrefix, CreatedAt, ParentId, Name, Type, SubType, Path)
                SELECT
                    new_id.Id,
                    substr(new_id.Id, 1, 5),
                    datetime('now'),
                    '{ContentFolderId}',
                    target.Name,
                    1,
                    101,
                    '/content/' || target.Name
                FROM target, new_id
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM DavItems
                    WHERE ParentId = '{ContentFolderId}'
                      AND Name = target.Name
                );
                """);

            // 5–6. Rename DavItem children that would collide under the target folder,
            // then reparent them out of the empty-named /content folder.
            // Suffix is " (xxxxx)" (8 chars); truncate Name to keep MaxLength(255).
            migrationBuilder.Sql($"""
                UPDATE DavItems
                SET Name = CASE
                    WHEN length(Name) + 8 > 255
                        THEN substr(Name, 1, 247) || ' (' || substr(Id, 1, 5) || ')'
                    ELSE Name || ' (' || substr(Id, 1, 5) || ')'
                END
                WHERE ParentId = (
                    SELECT Id
                    FROM DavItems
                    WHERE ParentId = '{ContentFolderId}'
                      AND Name = ''
                )
                AND EXISTS (
                    SELECT 1
                    FROM DavItems AS existing
                    WHERE existing.ParentId = (
                        SELECT Id
                        FROM DavItems
                        WHERE ParentId = '{ContentFolderId}'
                          AND Name = ({TargetCategorySql})
                    )
                    AND existing.Name = DavItems.Name
                );
                """);

            migrationBuilder.Sql($"""
                UPDATE DavItems
                SET ParentId = (
                    SELECT Id
                    FROM DavItems
                    WHERE ParentId = '{ContentFolderId}'
                      AND Name = ({TargetCategorySql})
                )
                WHERE ParentId = (
                    SELECT Id
                    FROM DavItems
                    WHERE ParentId = '{ContentFolderId}'
                      AND Name = ''
                );
                """);

            // 7–8. Guard the empty-folder delete: SQLite RAISE() only works inside
            // triggers, so install a temp BEFORE DELETE trigger that aborts when
            // children remain (those would be wiped by DavCleanupService after
            // TR_DavItems_DeleteDirectory). With zero children the delete proceeds;
            // cleanup enqueue for the folder Id itself is then a no-op.
            migrationBuilder.Sql($"""
                CREATE TEMP TRIGGER TR_FixEmptyCategories_AbortIfChildrenRemain
                BEFORE DELETE ON DavItems
                WHEN OLD.ParentId = '{ContentFolderId}'
                  AND OLD.Name = ''
                  AND EXISTS (
                      SELECT 1 FROM DavItems WHERE ParentId = OLD.Id
                  )
                BEGIN
                    SELECT RAISE(ABORT, 'Fix-Empty-Categories: empty category folder still has children');
                END;

                DELETE FROM DavItems
                WHERE ParentId = '{ContentFolderId}'
                  AND Name = '';

                DROP TRIGGER IF EXISTS TR_FixEmptyCategories_AbortIfChildrenRemain;
                """);

            // 9. Rebuild Path values after reparenting/renaming.
            AddPathToDavItem.BuildFullPath(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally left blank — data repair is not reversible.
        }
    }
}
