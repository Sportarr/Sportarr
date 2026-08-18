# Bazarr

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/bazarr.svg" alt="" width="72" height="72" />
</p>

Bazarr manages subtitles for your sports library. Add Sportarr in Bazarr exactly like you'd add Sonarr.

## Setup

1. In Bazarr, go to **Settings > Sonarr** and enable it
2. Set the **Address** and **Port** to your Sportarr host (e.g. your server IP and `1867`)
3. Paste your Sportarr API key (**Settings > General** in Sportarr), then test and save

Bazarr reads your leagues and events and searches for subtitles automatically.

!!! tip
    Sports releases often lack embedded subtitles entirely, so Bazarr's upgrade settings ("search until a better subtitle is found") work well here.
