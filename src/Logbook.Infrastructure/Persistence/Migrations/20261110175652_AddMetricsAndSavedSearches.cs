using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logbook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMetricsAndSavedSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF Core cannot create a partitioned table. The columns must match MetricPointConfiguration and the model
            // snapshot, which do not know about the partitioning.
            migrationBuilder.Sql("""
                CREATE TABLE telemetry.metrics (
                    "Id" uuid NOT NULL,
                    "Timestamp" timestamp with time zone NOT NULL,
                    "ApplicationId" uuid NOT NULL,
                    "Name" character varying(200) NOT NULL,
                    "Environment" character varying(100) NOT NULL,
                    "Count" bigint NOT NULL,
                    "Sum" double precision NOT NULL,
                    "Min" double precision NOT NULL,
                    "Max" double precision NOT NULL,
                    "Dimensions" jsonb,
                    CONSTRAINT "PK_metrics" PRIMARY KEY ("Timestamp", "Id")
                ) PARTITION BY RANGE ("Timestamp");
                """);

            // Creates missing partitions, each covering one day in UTC. See ensure_span_partitions for why it works this way.
            migrationBuilder.Sql("""
                CREATE FUNCTION telemetry.ensure_metric_partitions(days date[]) RETURNS void
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                AS $function$
                DECLARE
                    day date;
                    partition_name text;
                BEGIN
                    FOREACH day IN ARRAY days LOOP
                        partition_name := 'metrics_' || to_char(day, 'YYYYMMDD');

                        IF to_regclass(format('telemetry.%I', partition_name)) IS NULL THEN
                            PERFORM pg_advisory_xact_lock(hashtext('telemetry.metrics'));

                            EXECUTE format(
                                'CREATE TABLE IF NOT EXISTS telemetry.%I PARTITION OF telemetry.metrics FOR VALUES FROM (%L) TO (%L)',
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
                CREATE FUNCTION telemetry.drop_oldest_metric_partition_before(first_day_to_keep date) RETURNS date
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
                    WHERE pg_inherits.inhparent = 'telemetry.metrics'::regclass
                        AND child.relname ~ '^metrics_[0-9]{8}$'
                        AND child.relname::text < 'metrics_' || to_char(first_day_to_keep, 'YYYYMMDD')
                    ORDER BY child.relname
                    LIMIT 1;

                    IF partition_name IS NULL THEN
                        RETURN NULL;
                    END IF;

                    EXECUTE format('DROP TABLE telemetry.%I', partition_name);

                    RETURN to_date(substring(partition_name from 9), 'YYYYMMDD');
                END;
                $function$;
                """);

            migrationBuilder.CreateTable(
                name: "saved_searches",
                schema: "config",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Query = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_searches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_metrics_ApplicationId_Timestamp",
                schema: "telemetry",
                table: "metrics",
                columns: new[] { "ApplicationId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_metrics_Name_Timestamp",
                schema: "telemetry",
                table: "metrics",
                columns: new[] { "Name", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_saved_searches_Name",
                schema: "config",
                table: "saved_searches",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION telemetry.drop_oldest_metric_partition_before(date);");
            migrationBuilder.Sql("DROP FUNCTION telemetry.ensure_metric_partitions(date[]);");

            // Also drops all partitions.
            migrationBuilder.DropTable(
                name: "metrics",
                schema: "telemetry");

            migrationBuilder.DropTable(
                name: "saved_searches",
                schema: "config");
        }
    }
}
