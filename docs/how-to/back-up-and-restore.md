# Back up and restore

All state of Logbook is in PostgreSQL: Applications, hashes of API Keys, the keys of the sessions, and Log Events. The containers hold nothing, so a backup of the database is a backup of Logbook. These commands use the setup from [Deploy to production](deploy-production.md), with PostgreSQL in the service `db`. Use your usual backup if you already have one for PostgreSQL, such as continuous archiving.

A backup contains your logs, which can include personal data, and everything needed to read them. It keeps Log Events longer than their [retention](manage-applications.md#change-the-retention), so give your backups a retention that fits as well.

## Back up everything

```sh
docker compose exec -T db pg_dump -U logbook -d logbook --format=custom > logbook-$(date +%F).dump
```

This does not stop Logbook, and the backup is consistent. It reads a lot, and it holds up the removal of expired partitions while it runs, so choose a quiet time.

## Back up only the configuration

Logs are often worth less than the space they take. Without them, the backup is small and quick, and a restored Logbook has its Applications and API Keys, so your applications keep working, but no past logs:

```sh
docker compose exec -T db pg_dump -U logbook -d logbook --format=custom \
  --exclude-table-data='telemetry.log_events*' > logbook-config-$(date +%F).dump
```

## Restore

Restore into an empty database. To replace the current one, this removes it for good, so first make sure the backup is the right one:

1. Stop the server, and remove the old database with its volume:

   ```sh
   docker compose stop logbook
   docker compose down --volumes
   ```

2. Start only the database, which creates an empty one, and restore:

   ```sh
   docker compose up -d db
   docker compose exec -T db pg_restore -U logbook -d logbook --exit-on-error --no-owner --role=logbook < logbook-2026-11-06.dump
   ```

3. Use the image of the version the backup was made with, or a newer one, and start everything. The migrator brings the schema up to date:

   ```sh
   docker compose up -d
   ```

4. Check that `/health` says `Healthy`, and that you find your Applications and Log Events.

People who were signed in stay signed in if the backup is recent enough. Applications keep working, as their API Keys are in the backup, except for those issued after it.

The first [retention run](../reference/configuration.md#retention) starts with the server, and removes Log Events that are past their retention by now. To look at old logs in a backup, restore it with a longer [`Retention:DefaultDays`](../reference/configuration.md#retention), and check the Applications with a retention of their own.

## Restore to another database user

If the user has another name than `logbook`, set it in `--role`. It must be the user that the migrator uses, as it owns the tables and the functions that create and drop partitions.
