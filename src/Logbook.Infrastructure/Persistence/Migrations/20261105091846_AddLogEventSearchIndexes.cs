using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logbook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLogEventSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_log_events_ApplicationId_Timestamp_Id",
                schema: "telemetry",
                table: "log_events",
                columns: new[] { "ApplicationId", "Timestamp", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_log_events_Environment_Timestamp_Id",
                schema: "telemetry",
                table: "log_events",
                columns: new[] { "Environment", "Timestamp", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_log_events_Timestamp_Id_WarningOrAbove",
                schema: "telemetry",
                table: "log_events",
                columns: new[] { "Timestamp", "Id" },
                filter: "\"Level\" >= 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_log_events_ApplicationId_Timestamp_Id",
                schema: "telemetry",
                table: "log_events");

            migrationBuilder.DropIndex(
                name: "IX_log_events_Environment_Timestamp_Id",
                schema: "telemetry",
                table: "log_events");

            migrationBuilder.DropIndex(
                name: "IX_log_events_Timestamp_Id_WarningOrAbove",
                schema: "telemetry",
                table: "log_events");
        }
    }
}
