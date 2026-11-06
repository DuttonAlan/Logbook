using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logbook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLogEventTraceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_log_events_TraceId_Timestamp_Id",
                schema: "telemetry",
                table: "log_events",
                columns: new[] { "TraceId", "Timestamp", "Id" },
                filter: "\"TraceId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_log_events_TraceId_Timestamp_Id",
                schema: "telemetry",
                table: "log_events");
        }
    }
}
