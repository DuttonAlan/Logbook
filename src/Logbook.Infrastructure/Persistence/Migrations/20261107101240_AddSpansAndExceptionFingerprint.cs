using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logbook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpansAndExceptionFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExceptionFingerprint",
                schema: "telemetry",
                table: "log_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // EF Core cannot create a partitioned table. The columns must match TraceSpanConfiguration and the model
            // snapshot, which do not know about the partitioning.
            migrationBuilder.Sql("""
                CREATE TABLE telemetry.spans (
                    "Id" uuid NOT NULL,
                    "Timestamp" timestamp with time zone NOT NULL,
                    "ApplicationId" uuid NOT NULL,
                    "Kind" smallint NOT NULL,
                    "Name" character varying(500) NOT NULL,
                    "DurationMs" double precision NOT NULL,
                    "Success" boolean NOT NULL,
                    "Environment" character varying(100) NOT NULL,
                    "TraceId" character varying(64) NOT NULL,
                    "SpanId" character varying(64) NOT NULL,
                    "ParentSpanId" character varying(64),
                    "StatusCode" integer,
                    "DependencyType" character varying(100),
                    "Target" character varying(500),
                    "Data" character varying(8192),
                    CONSTRAINT "PK_spans" PRIMARY KEY ("Timestamp", "Id")
                ) PARTITION BY RANGE ("Timestamp");
                """);

            // Creates missing partitions, each covering one day in UTC. The application user needs no rights to change
            // the schema: SECURITY DEFINER runs this as the owner, the user that applied the migrations. The lock makes
            // concurrent callers wait instead of failing when they create the same partition.
            migrationBuilder.Sql("""
                CREATE FUNCTION telemetry.ensure_span_partitions(days date[]) RETURNS void
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                AS $function$
                DECLARE
                    day date;
                    partition_name text;
                BEGIN
                    FOREACH day IN ARRAY days LOOP
                        partition_name := 'spans_' || to_char(day, 'YYYYMMDD');

                        IF to_regclass(format('telemetry.%I', partition_name)) IS NULL THEN
                            PERFORM pg_advisory_xact_lock(hashtext('telemetry.spans'));

                            EXECUTE format(
                                'CREATE TABLE IF NOT EXISTS telemetry.%I PARTITION OF telemetry.spans FOR VALUES FROM (%L) TO (%L)',
                                partition_name,
                                to_char(day, 'YYYY-MM-DD') || ' 00:00:00+00',
                                to_char(day + 1, 'YYYY-MM-DD') || ' 00:00:00+00');
                        END IF;
                    END LOOP;
                END;
                $function$;
                """);

            // Drops the partition of the oldest day before the given day, and returns that day, or NULL if there is none.
            // See drop_oldest_log_event_partition_before for why it works this way.
            migrationBuilder.Sql("""
                CREATE FUNCTION telemetry.drop_oldest_span_partition_before(first_day_to_keep date) RETURNS date
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                SET lock_timeout = '5s'
                AS $function$
                DECLARE
                    partition_name text;
                BEGIN
                    SELECT child.relname INTO partition_name
                    FROM pg_inherits
                    JOIN pg_class child ON child.oid = pg_inherits.inhrelid
                    WHERE pg_inherits.inhparent = 'telemetry.spans'::regclass
                        AND child.relname ~ '^spans_[0-9]{8}$'
                        AND child.relname::text < 'spans_' || to_char(first_day_to_keep, 'YYYYMMDD')
                    ORDER BY child.relname
                    LIMIT 1;

                    IF partition_name IS NULL THEN
                        RETURN NULL;
                    END IF;

                    EXECUTE format('DROP TABLE telemetry.%I', partition_name);

                    RETURN to_date(substring(partition_name from 7), 'YYYYMMDD');
                END;
                $function$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_log_events_ExceptionFingerprint_Timestamp_Id",
                schema: "telemetry",
                table: "log_events",
                columns: new[] { "ExceptionFingerprint", "Timestamp", "Id" },
                filter: "\"ExceptionFingerprint\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_log_events_Timestamp_ExceptionGroup",
                schema: "telemetry",
                table: "log_events",
                columns: new[] { "Timestamp", "ApplicationId", "ExceptionFingerprint" },
                filter: "\"ExceptionFingerprint\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_spans_ApplicationId_Timestamp_Id",
                schema: "telemetry",
                table: "spans",
                columns: new[] { "ApplicationId", "Timestamp", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_spans_Kind_Timestamp_Id",
                schema: "telemetry",
                table: "spans",
                columns: new[] { "Kind", "Timestamp", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_spans_Timestamp_Id_Failed",
                schema: "telemetry",
                table: "spans",
                columns: new[] { "Timestamp", "Id" },
                filter: "NOT \"Success\"");

            migrationBuilder.CreateIndex(
                name: "IX_spans_TraceId_Timestamp_Id",
                schema: "telemetry",
                table: "spans",
                columns: new[] { "TraceId", "Timestamp", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION telemetry.drop_oldest_span_partition_before(date);");
            migrationBuilder.Sql("DROP FUNCTION telemetry.ensure_span_partitions(date[]);");

            // Also drops all partitions.
            migrationBuilder.DropTable(
                name: "spans",
                schema: "telemetry");

            migrationBuilder.DropIndex(
                name: "IX_log_events_ExceptionFingerprint_Timestamp_Id",
                schema: "telemetry",
                table: "log_events");

            migrationBuilder.DropIndex(
                name: "IX_log_events_Timestamp_ExceptionGroup",
                schema: "telemetry",
                table: "log_events");

            migrationBuilder.DropColumn(
                name: "ExceptionFingerprint",
                schema: "telemetry",
                table: "log_events");
        }
    }
}
