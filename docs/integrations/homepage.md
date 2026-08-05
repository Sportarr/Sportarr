# Homepage

[Homepage](https://gethomepage.dev) can show a Sportarr tile with your library counts.

## Native widget

A first-class Sportarr widget is merged into Homepage and arrives with their next release. Once you're on a Homepage version that includes it:

```yaml
- Media:
    - Sportarr:
        icon: mdi-trophy
        href: http://your.server:1867
        widget:
          type: sportarr
          url: http://your.server:1867
          key: yourapikey
```

It shows Wanted, Queued, and Leagues, matching the look of the other arr widgets.

## customapi widget (works on any Homepage version)

Until then, or on older Homepage versions, the built-in `customapi` widget gets you the same tile using Sportarr's `/api/stats` endpoint:

```yaml
- Media:
    - Sportarr:
        icon: mdi-trophy
        href: http://your.server:1867
        widget:
          type: customapi
          url: http://your.server:1867/api/stats
          headers:
            X-Api-Key: yourapikey
          mappings:
            - field: wanted
              label: Wanted
              format: number
            - field: queued
              label: Queued
              format: number
            - field: leagues
              label: Leagues
              format: number
```

Get your API key from **Settings > General**. The endpoint also exposes `events`, `monitored`, `downloaded`, and `files` if you'd rather show different counts (Homepage displays up to four).
