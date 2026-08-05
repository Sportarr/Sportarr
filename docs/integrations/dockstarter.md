# DockSTARTer

[DockSTARTer](https://dockstarter.com/) makes it easy to run a home server
stack in Docker, and Sportarr ships as a built-in app template alongside the
other *arr apps.

## Adding Sportarr

From the DockSTARTer CLI:

```bash
ds -a sportarr
ds -c
```

Or run `ds` and enable Sportarr from the app selection menu, then let
DockSTARTer regenerate and start the compose stack.

The template follows the standard DockSTARTer conventions:

- Web UI on port `1867` (change with `SPORTARR__PORT_1867`)
- Config stored in `${DOCKER_VOLUME_CONFIG}/sportarr`
- Media paths available under the shared `/storage` mounts

Point your root folders and download client paths at locations under
`/storage` so imports can hardlink instead of copy.

## Database options

Sportarr uses its own SQLite database inside the config folder by default,
so there is nothing to configure. To run against PostgreSQL instead, set
`Sportarr__Database__Provider` to `postgres` in the app environment file
(`.env.app.sportarr`) and fill in the `Sportarr__Database__*` connection
settings. Any other value, including blank, keeps SQLite. See
[Database](../getting-started/database.md) for details; PostgreSQL is
supported for fresh installs only.

## Multiple instances

DockSTARTer instance names work as expected. For example
`ds -a sportarr__4k` adds a second, independent Sportarr instance with its
own config folder and its own SQLite database.
