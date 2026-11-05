using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logbook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLogEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "telemetry");

            // EF Core cannot create a partitioned table. The columns must match LogEventConfiguration and the model
            // snapshot, which do not know about the partitioning.
            migrationBuilder.Sql("""
                CREATE TABLE telemetry.log_events (
                    "Id" uuid NOT NULL,
                    "Timestamp" timestamp with time zone NOT NULL,
                    "ApplicationId" uuid NOT NULL,
                    "Level" smallint NOT NULL,
                    "Message" text NOT NULL,
                    "MessageTemplate" text,
                    "Category" character varying(500),
                    "Environment" character varying(100) NOT NULL,
                    "EventId" integer,
                    "Exception" jsonb,
                    "TraceId" character varying(64),
                    "SpanId" character varying(64),
                    "Properties" jsonb,
                    CONSTRAINT "PK_log_events" PRIMARY KEY ("Timestamp", "Id")
                ) PARTITION BY RANGE ("Timestamp");
                """);

            // Creates missing partitions, each covering one day in UTC. The application user needs no rights to change
            // the schema: SECURITY DEFINER runs this as the owner, the user that applied the migrations. The lock makes
            // concurrent callers wait instead of failing when they create the same partition.
            migrationBuilder.Sql("""
                CREATE FUNCTION telemetry.ensure_log_event_partitions(days date[]) RETURNS void
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                AS $function$
                DECLARE
                    day date;
                    partition_name text;
                BEGIN
                    FOREACH day IN ARRAY days LOOP
                        partition_name := 'log_events_' || to_char(day, 'YYYYMMDD');

                        IF to_regclass(format('telemetry.%I', partition_name)) IS NULL THEN
                            PERFORM pg_advisory_xact_lock(hashtext('telemetry.log_events'));

                            EXECUTE format(
                                'CREATE TABLE IF NOT EXISTS telemetry.%I PARTITION OF telemetry.log_events FOR VALUES FROM (%L) TO (%L)',
                                partition_name,
                                to_char(day, 'YYYY-MM-DD') || ' 00:00:00+00',
                                to_char(day + 1, 'YYYY-MM-DD') || ' 00:00:00+00');
                        END IF;
                    END LOOP;
                END;
                $function$;
                """);

            // The search covers the exact words, because logs are full of identifiers and mix languages, so no stemming.
            // EF Core has no model for an index on an expression, so it is not part of LogEventConfiguration.
            migrationBuilder.Sql("""
                CREATE INDEX "IX_log_events_Message_FullText" ON telemetry.log_events USING gin (to_tsvector('simple', "Message"));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_log_events_Exception",
                schema: "telemetry",
                table: "log_events",
                column: "Exception")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_log_events_Properties",
                schema: "telemetry",
                table: "log_events",
                column: "Properties")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION telemetry.ensure_log_event_partitions(date[]);");

            // Also drops all partitions.
            migrationBuilder.DropTable(
                name: "log_events",
                schema: "telemetry");
        }
    }
}
