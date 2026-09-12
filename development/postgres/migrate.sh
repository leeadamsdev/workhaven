#!/bin/sh
set -eu

sh /database/init-app-user.sh
exec psql --username "${POSTGRES_USER:-postgres}" --dbname "$POSTGRES_DB" \
    --set ON_ERROR_STOP=1 --file /database/migrations.sql
