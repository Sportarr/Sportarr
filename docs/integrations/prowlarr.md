# Prowlarr

Prowlarr manages your indexers in one place and syncs them to Sportarr automatically.

!!! info "Native Sportarr support is on its way"
    A native Sportarr application type is [in review at Prowlarr](https://github.com/Prowlarr/Prowlarr/pull/2758). Once it ships, you'll add Sportarr directly under Settings > Apps with full sync, and this page will be updated. Until then, the Sonarr application type below works today.

## Setup (current method)

1. In Prowlarr, go to **Settings > Apps**
2. Add **Sonarr** as an application
3. Use `http://localhost:1867` as the Sportarr URL (or your actual IP/hostname)
4. Get your API key from Sportarr's **Settings > General**
5. Select **TV (5000)** categories for sync, which includes TV/HD (5040), TV/UHD (5045), and TV/Sport (5060)

Indexers sync automatically and stay updated when you change them in Prowlarr.

!!! tip "Docker networking"
    When both apps run in Docker on the same network, use container names instead of `localhost`, e.g. `http://sportarr:1867` and `http://prowlarr:9696`.

## Categories

`5060` (TV/Sport) is the primary sports category. Some indexers file sports content under the Movies subcategories (`2010`-`2060`), which is why Sportarr's own defaults include both. Sportarr tolerates over-broad category lists; its matcher filters out non-sports results.
