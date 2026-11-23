-- =============================================================================
-- PostgreSQL role bootstrap (idempotent).
--
-- Runs INSIDE the VNet as the first step of the migration job, connected as
-- the migrator managed identity (an Entra administrator of the server):
--
--   export PGPASSWORD="$(az account get-access-token \
--       --resource-type oss-rdbms --query accessToken -o tsv)"   # MI token
--   psql "host=$Database__Host dbname=postgres user=$Database__Username sslmode=verify-full" \
--        -v app_role="$Database__AppRoleName" -v app_object_id="$Database__AppRoleObjectId" -v app_database="$Database__Name" \
--        -v migrator_role="$Database__Username" \
--        -f bootstrap-roles.sql
--
-- Then the EF Core migration bundle runs as the same identity and owns every
-- table it creates. The API identity only gets DML (no DDL).
-- =============================================================================

\set ON_ERROR_STOP on

SELECT set_config('bootstrap.app_role', :'app_role', false);
SELECT set_config('bootstrap.migrator_role', :'migrator_role', false);
SELECT set_config('bootstrap.app_object_id', :'app_object_id', false);

-- 1. Map the API managed identity to a database role (no password exists).
DO $$
DECLARE
  app_role text := current_setting('bootstrap.app_role');
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = app_role) THEN
    PERFORM pg_catalog.pgaadauth_create_principal_with_oid(app_role, current_setting('bootstrap.app_object_id')::uuid::text, 'service', false, false);
  END IF;
END
$$;

-- Map existing roles too, preserving ownership and grants after identity replacement.
SELECT format('SECURITY LABEL FOR pgaadauth ON ROLE %I IS %L', :'app_role',
  'aadauth,oid=' || (:'app_object_id')::uuid::text || ',type=service') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', :'app_database', :'app_role') \gexec
\connect :app_database
SELECT set_config('bootstrap.app_role', :'app_role', false);
SELECT set_config('bootstrap.migrator_role', :'migrator_role', false);

-- 2. Lock down the public schema.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- 3. Logical schemas. EF Core migrations own the tables inside them
--    (EnsureSchema is idempotent, so pre-creating them here is safe).
DO $$
DECLARE
  s             text;
  app_role      text := current_setting('bootstrap.app_role');
  migrator_role text := current_setting('bootstrap.migrator_role');
BEGIN
  FOREACH s IN ARRAY ARRAY['myvedicstar', 'myvedicstarsettings', 'myvedicstarvedicprofiles', 'myvedicstarkundali', 'identity', 'profile', 'astrology', 'matching', 'matrimony'] LOOP
    EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION %I', s, migrator_role);
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', s, app_role);
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', s, app_role);
    EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', s, app_role);
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', migrator_role, s, app_role);
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO %I', migrator_role, s, app_role);
  END LOOP;
END
$$;

-- 3b. KUN-17 (api#57): KundaliReports.
--     Named explicitly, not left to the loop above. GRANT ... ON ALL TABLES only covers tables that exist when it
--     runs, and the loop's ALTER DEFAULT PRIVILEGES only covers tables a LATER migration creates. The alerts table
--     proved that insufficient in the vedic-profiles outage: every test was green and the endpoint returned 500 on a
--     permission error at runtime. An explicit grant that names the table cannot be missed by reading this script.
DO $
DECLARE
  app_role text := current_setting('bootstrap.app_role');
BEGIN
  IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'myvedicstarkundali')
     AND EXISTS (SELECT 1 FROM information_schema.tables
                 WHERE table_schema = 'myvedicstarkundali' AND table_name = 'KundaliReports') THEN
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON myvedicstarkundali.%I TO %I', 'KundaliReports', app_role);
  END IF;
END
$;

-- 4. Schema-qualified DML re-grant for tables that exist BEFORE this script runs.
--    The loop above also sets ALTER DEFAULT PRIVILEGES, which covers every table a later migration creates. That is
--    insufficient in one case: a role that already existed was not covered by a default privilege set before the
--    table was created, and GRANT ... ON ALL TABLES only sees tables that exist AT THE TIME IT RUNS. Re-running this
--    grant after migrations therefore makes the app role's DML rights converge, whatever order the server was
--    provisioned in. KUN-13's KundaliAlertPreferences table depends on this: without it the alerts endpoints return
--    500 with a permission error, which is exactly the failure the vedic-profiles outage produced. KUN-15's
--    KundaliInterpretations table (KUN-14, api#54) depends on it for the same reason: it is created by a later
--    migration in myvedicstarkundali, and the interpretations read path queries it directly. An endpoint that
--    returns 500 with a permission error after a clean deploy is this omission, and every test still passes.
--
--    KundaliAshtakootaComparisons table depends on it for the same reason: it is created by a later migration in
--    myvedicstarkundali, and the GET comparisons route reads and writes it on every request.
DO $$
DECLARE
  s             text;
  app_role      text := current_setting('bootstrap.app_role');
BEGIN
  FOREACH s IN ARRAY ARRAY['myvedicstar', 'myvedicstarsettings', 'myvedicstarvedicprofiles', 'myvedicstarkundali'] LOOP
    IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = s) THEN
      EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', s, app_role);
      EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', s, app_role);
      EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', s, app_role);
    END IF;
  END LOOP;
END
$$;
