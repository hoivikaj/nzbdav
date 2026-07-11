using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.MetricsMigrations
{
    /// <inheritdoc />
    public partial class AddFailoverEdges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FailoverHourly",
                columns: table => new
                {
                    Hour = table.Column<long>(type: "INTEGER", nullable: false),
                    FromProvider = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ToProvider = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailoverHourly", x => new { x.Hour, x.FromProvider, x.ToProvider, x.Reason });
                });

            migrationBuilder.CreateTable(
                name: "FailoverMisses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    FromProvider = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ToProvider = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailoverMisses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FailoverMisses_At",
                table: "FailoverMisses",
                column: "At");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FailoverHourly");

            migrationBuilder.DropTable(
                name: "FailoverMisses");
        }
    }
}
