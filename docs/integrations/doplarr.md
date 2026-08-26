# Doplarr

[Doplarr](https://github.com/activexray/doplarr_rs) is a Discord request bot for the arr apps. It gives your server members a slash command to search a catalog and add what they want, without handing anyone access to the apps themselves. It has native Sportarr support through a `/request sport` command.

| | |
|---|---|
| Command | `/request sport` |
| Authentication | Sportarr URL and API key |
| What gets added | Leagues, added monitored |

!!! info "Availability"
    Sportarr support is merged in Doplarr and ships with their next release after v4.6.0.

## Setup

Add a Sportarr backend to your Doplarr `config.toml`:

```toml
[[backends]]
media = "sport"

[backends.config.Sportarr]
url = "http://localhost:1867"
api_key = "${SPORTARR_API_KEY}"
```

Your API key is in Sportarr under **Settings > General**. The `${VAR}` syntax pulls the value from an environment variable, which keeps the key out of the config file.

## Optional settings

```toml
quality_profile = "WEB-1080p"
```

Pinning `quality_profile` applies that profile to every request and removes the dropdown from Discord. The name must match a profile in Sportarr exactly. Leave it out and the requester picks a profile at request time.

Root folder and monitoring scope are never asked in Discord. Both follow Sportarr's own defaults, which are the per-root-folder default profile and the league's Future monitor type.

## How a request works

1. A member runs `/request sport` and types a search term
2. Doplarr searches the Sportarr league catalog and shows the matches as a dropdown, with the country and founding year alongside each name
3. The member picks a league, and a quality profile if one is not pinned
4. Sportarr adds the league monitored, so its new events are grabbed as they air

Doplarr cross-references your library on every search, so leagues you already have are recognised rather than offered again.

## Leagues you already have

Two cases are handled deliberately, and both stop the request early rather than changing your library behind your back.

A league that is **already added and monitored** is reported back as already monitored. Nothing is sent to Sportarr, because nothing needs to change.

A league that is **already added but unmonitored** is refused, with a message telling the requester to enable monitoring in Sportarr. Unmonitoring a league is a deliberate library decision that may have been made for a reason, so a Discord request will not silently reverse it. Re-enable it in Sportarr under the league itself.
