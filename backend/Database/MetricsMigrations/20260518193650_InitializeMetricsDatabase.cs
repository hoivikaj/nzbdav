using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.MetricsMigrations
{
    /// <inheritdoc />
    public partial class InitializeMetricsDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogueDaily",
                columns: table => new
                {
                    Day = table.Column<long>(type: "INTEGER", nullable: false),
                    FileCount = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    AddedCount = table.Column<long>(type: "INTEGER", nullable: false),
                    RemovedCount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueDaily", x => x.Day);
                });

            migrationBuilder.CreateTable(
                name: "MetricEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RefId = table.Column<string>(type: "TEXT", nullable: true),
                    Tag1 = table.Column<string>(type: "TEXT", nullable: true),
                    Tag2 = table.Column<string>(type: "TEXT", nullable: true),
                    Num = table.Column<long>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderHourly",
                columns: table => new
                {
                    Hour = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Articles = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesFetched = table.Column<long>(type: "INTEGER", nullable: false),
                    Errors = table.Column<long>(type: "INTEGER", nullable: false),
                    Retries = table.Column<long>(type: "INTEGER", nullable: false),
                    SumDurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    P95DurationMs = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderHourly", x => new { x.Hour, x.Provider });
                });

            migrationBuilder.CreateTable(
                name: "ProviderMinutes",
                columns: table => new
                {
                    Minute = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Articles = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesFetched = table.Column<long>(type: "INTEGER", nullable: false),
                    Errors = table.Column<long>(type: "INTEGER", nullable: false),
                    Retries = table.Column<long>(type: "INTEGER", nullable: false),
                    SumDurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Hist = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderMinutes", x => new { x.Minute, x.Provider });
                });

            migrationBuilder.CreateTable(
                name: "ReadSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    BytesServed = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesFetched = table.Column<long>(type: "INTEGER", nullable: false),
                    ClientUserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    ClientIp = table.Column<string>(type: "TEXT", nullable: true),
                    EndReason = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SegmentFetches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ReadSessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    QueueItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Retries = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentFetches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThroughputMinutes",
                columns: table => new
                {
                    Minute = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesServed = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesFetched = table.Column<long>(type: "INTEGER", nullable: false),
                    Articles = table.Column<long>(type: "INTEGER", nullable: false),
                    Errors = table.Column<long>(type: "INTEGER", nullable: false),
                    ActiveReadsMax = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThroughputMinutes", x => x.Minute);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetricEvents_Kind_At",
                table: "MetricEvents",
                columns: new[] { "Kind", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadSessions_Path",
                table: "ReadSessions",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_ReadSessions_StartedAt",
                table: "ReadSessions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentFetches_At",
                table: "SegmentFetches",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentFetches_Provider_At",
                table: "SegmentFetches",
                columns: new[] { "Provider", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogueDaily");

            migrationBuilder.DropTable(
                name: "MetricEvents");

            migrationBuilder.DropTable(
                name: "ProviderHourly");

            migrationBuilder.DropTable(
                name: "ProviderMinutes");

            migrationBuilder.DropTable(
                name: "ReadSessions");

            migrationBuilder.DropTable(
                name: "SegmentFetches");

            migrationBuilder.DropTable(
                name: "ThroughputMinutes");
        }
    }
}
