#!/bin/sh
set -eu

: "${POSTGRES_APP_PASSWORD:?POSTGRES_APP_PASSWORD must be set}"

psql --username "${POSTGRES_USER:-postgres}" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1 <<'SQL'
\getenv app_password POSTGRES_APP_PASSWORD
BEGIN;
SELECT 'CREATE ROLE workhaven_app LOGIN'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'workhaven_app')
\gexec
ALTER ROLE workhaven_app WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'app_password';
SELECT format('GRANT CONNECT ON DATABASE %I TO workhaven_app', current_database())
\gexec
COMMIT;
SQL
