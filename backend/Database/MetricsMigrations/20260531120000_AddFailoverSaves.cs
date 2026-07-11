using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.MetricsMigrations
{
    /// <inheritdoc />
    public partial class AddFailoverSaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FailoverSaves",
                table: "ProviderMinutes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "FailoverSaves",
                table: "ProviderHourly",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "FailoverSaves",
                table: "ReadSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailoverSaves",
                table: "ProviderMinutes");

            migrationBuilder.DropColumn(
                name: "FailoverSaves",
                table: "ProviderHourly");

            migrationBuilder.DropColumn(
                name: "FailoverSaves",
                table: "ReadSessions");
        }
    }
}
