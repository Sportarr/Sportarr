# Database

Sportarr uses SQLite by default with no configuration needed. If you already run a PostgreSQL cluster elsewhere in your setup, Sportarr can use it instead.

!!! warning "Fresh installs only"
    PostgreSQL support is for fresh installs only. There is no SQLite to PostgreSQL migration path. Pick your provider before your first run.

## PostgreSQL configuration

`docker-compose.yml` ships with the PostgreSQL environment variables as commented-out lines you can uncomment, and `docker-compose.example.yml` has a full working example using Docker secrets for the password.

| Variable | Purpose |
|---|---|
| `Sportarr__Database__Provider` | `postgres` (omit or `sqlite` for the default) |
| `Sportarr__Database__Host` | Postgres server hostname |
| `Sportarr__Database__Port` | Postgres port (default `5432`) |
| `Sportarr__Database__Name` | Database name |
| `Sportarr__Database__Username` | Database user |
| `Sportarr__Database__Password` | Database password |
| `Sportarr__Database__ConnectionString` | Full connection string, overrides the individual fields above if set |

```yaml
environment:
  - Sportarr__Database__Provider=postgres
  - Sportarr__Database__Host=postgres
  - Sportarr__Database__Port=5432
  - Sportarr__Database__Name=sportarr
  - Sportarr__Database__Username=sportarr
  - Sportarr__Database__Password=change-me
```

## Docker secrets

Any `Sportarr__*` environment variable can instead be supplied from a file by prefixing it with `FILE__` and pointing the value at the file's path:

```yaml
environment:
  - FILE__Sportarr__Database__Password=/run/secrets/sportarr_db_password
secrets:
  sportarr_db_password:
    file: ./secrets/sportarr_db_password.txt
```

## Backups

Backup and restore work the same way on both providers (`pg_dump`/`pg_restore` under the hood for Postgres), but a backup can only be restored onto an install running the same provider it was created on.
