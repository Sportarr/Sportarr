# Homepage

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/homepage.webp" alt="" width="72" height="72" />
</p>

[Homepage](https://gethomepage.dev) can show a Sportarr tile with your library counts.

## Native widget (Homepage v2.0.0 and later)

Homepage ships a first-class Sportarr widget since v2.0.0. Add it to your services like any other arr widget:

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

It shows Wanted, Queued, and Leagues, matching the look of the other arr widgets. The full reference lives in [Homepage's widget docs](https://gethomepage.dev/widgets/services/sportarr/).

## customapi widget (Homepage versions before v2.0.0)

On older Homepage versions, the built-in `customapi` widget gets you the same tile using Sportarr's `/api/stats` endpoint:

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
