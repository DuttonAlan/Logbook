# Upgrade Logbook

Logbook never changes its database at startup. An upgrade replaces the two images and runs the migrator in between. These steps use the setup from [Deploy to production](deploy-production.md).

## Upgrade the server

1. Read the [changelog](../../CHANGELOG.md) from your version up to the new one. Look for *Changed* and *Removed*, and for migrations that take long on a large table.

2. [Back up](back-up-and-restore.md) the database. A migration cannot be undone by going back to the old image.

3. Set the new version in the tags of both images in `compose.yaml`. If you use a tag such as `1.0`, it moves by itself, so only pull.

4. Stop the server, so that it writes the Log Events in its queue and nothing uses the old schema during the migration:

   ```sh
   docker compose stop logbook
   ```

   Applications keep their Log Events in the buffer of the SDK and send them again later, up to [its limits](../reference/sdk-behavior.md).

5. Pull the images, then start. Compose runs the migrator first, and only starts the server if that succeeds:

   ```sh
   docker compose pull
   docker compose up -d
   ```

6. Check the result. `migrations` should be `Healthy`, and `version` should show the new version:

   ```sh
   curl -H "Authorization: Bearer $MONITORING_KEY" https://logbook.example.com/health/details
   ```

With several servers, stop all of them in step 4.

## If the migrator fails

The server does not start, and the old schema, or one with some of the migrations applied, is still there. Read the reason with `docker compose logs migrate`. Each migration is applied in its own transaction, so run `docker compose up -d` again once the cause is fixed.

## Go back to an older version

1. Stop the server.
2. Restore the backup from before the upgrade, as described in [Back up and restore](back-up-and-restore.md#restore). Log Events that arrived since then are lost.
3. Set the old version in `compose.yaml`, and start.

Starting an older server on a newer database without restoring is not supported. It shows up as `Degraded` in `/health`, as the [migrations check](../reference/health-checks.md#checks) finds migrations it does not know, and the server may fail.

## Upgrade the SDK

The packages `Logbook.Sdk` and `Logbook.Extensions.Logging` have their own version in your application, and do not have to match the server. Upgrading the server first is the safe order: within a major version the log API stays compatible with older SDKs.
