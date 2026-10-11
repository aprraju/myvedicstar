START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM myvedicstar."__EFMigrationsHistory" WHERE "MigrationId" = '20261011000000_AddHybridCacheEntries') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'myvedicstar') THEN
            CREATE SCHEMA myvedicstar;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM myvedicstar."__EFMigrationsHistory" WHERE "MigrationId" = '20261011000000_AddHybridCacheEntries') THEN
    CREATE TABLE myvedicstar.cache_entries (
        id VARCHAR(449) COLLATE "C" PRIMARY KEY,
        value BYTEA NOT NULL,
        expiresattime TIMESTAMPTZ NOT NULL,
        slidingexpirationinseconds BIGINT NULL,
        absoluteexpiration TIMESTAMPTZ NULL
    );
    CREATE INDEX ix_cache_entries_expiresattime ON myvedicstar.cache_entries (expiresattime);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM myvedicstar."__EFMigrationsHistory" WHERE "MigrationId" = '20261011000000_AddHybridCacheEntries') THEN
    INSERT INTO myvedicstar."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261011000000_AddHybridCacheEntries', '10.0.12');
    END IF;
END $EF$;
COMMIT;

