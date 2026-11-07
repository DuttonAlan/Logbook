using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logbook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionAndDataProtectionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetentionDays",
                schema: "config",
                table: "applications",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "config",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                });

            // Drops the partition of the oldest day before the given day, and returns that day, or NULL if there is none.
            // Only one per call, as its lock on the table of all Log Events lasts until the transaction ends. The
            // application user needs no rights to change the schema: SECURITY DEFINER runs this as the owner, the user that
            // applied the migrations. A search can hold the table for a while, and everything else would queue up behind a
            // waiting DROP, so it gives up after a moment and the next retention run tries again. The names compare like
            // their days, as they have a fixed length.
            migrationBuilder.Sql("""
                CREATE FUNCTION telemetry.drop_oldest_log_event_partition_before(first_day_to_keep date) RETURNS date
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
                    WHERE pg_inherits.inhparent = 'telemetry.log_events'::regclass
                        AND child.relname ~ '^log_events_[0-9]{8}$'
                        AND child.relname::text < 'log_events_' || to_char(first_day_to_keep, 'YYYYMMDD')
                    ORDER BY child.relname
                    LIMIT 1;

                    IF partition_name IS NULL THEN
                        RETURN NULL;
                    END IF;

                    EXECUTE format('DROP TABLE telemetry.%I', partition_name);

                    RETURN to_date(substring(partition_name from 12), 'YYYYMMDD');
                END;
                $function$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION telemetry.drop_oldest_log_event_partition_before(date);");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "config");

            migrationBuilder.DropColumn(
                name: "RetentionDays",
                schema: "config",
                table: "applications");
        }
    }
}
