#!/usr/bin/env bash
set -euo pipefail

# Run seed-progress.sql against the MCP SQL Server from docker-compose.mcp.yml
# Assumes docker-compose.mcp.yml is in repo root and services are up.

SQL_CONTAINER="ato-copilot-sql"
SQL_FILE="/var/opt/mssql/seed-progress.sql"
LOCAL_SQL_PATH="scripts/seed-progress.sql"

if [ ! -f "$LOCAL_SQL_PATH" ]; then
  echo "scripts/seed-progress.sql not found in repo; aborting" >&2
  exit 1
fi

# Copy seed into container and run sqlcmd
docker cp "$LOCAL_SQL_PATH" "$SQL_CONTAINER":/tmp/seed-progress.sql

docker exec -u 0 $SQL_CONTAINER /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -i /tmp/seed-progress.sql
