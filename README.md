<div align="center">

<p>
  <a href="https://github.com/Sportarr/Sportarr/blob/main/COPYRIGHT.md"><img src="https://img.shields.io/badge/license-GPL--v3-green?style=flat" alt="License"></a>
  <a href="https://discord.gg/YjHVWGWjjG"><img src="https://img.shields.io/badge/discord-join-7289da?style=flat&logo=discord&logoColor=white" alt="Discord"></a>
</p>

<img src="./Logo/512.png" width="200" alt="Sportarr">

<h3>Sports PVR for Usenet and Torrents</h3>

<p>Like Sonarr &amp; Radarr, but for sports events. Monitors sports leagues, searches your indexers<br>for releases, and handles file renaming, organization, and media server integration.</p>

<p>
  <a href="https://sportarr.net"><img src="https://img.shields.io/badge/website-sportarr.net-blue?style=flat" alt="Website"></a>
  <a href="https://hub.docker.com/r/sportarr/sportarr"><img src="https://img.shields.io/badge/docker-sportarr%2Fsportarr-2496ED?style=flat&logo=docker&logoColor=white" alt="Docker"></a>
  <img src="https://img.shields.io/badge/arch-amd64%20%7C%20arm64-orange?style=flat" alt="Architecture">
  <a href="https://github.com/Sportarr/Sportarr/releases/latest"><img src="https://img.shields.io/github/v/release/Sportarr/Sportarr?style=flat&label=release&color=blueviolet" alt="Latest release"></a>
  <a href="https://github.com/Sportarr/Sportarr/stargazers"><img src="https://img.shields.io/github/stars/Sportarr/Sportarr?style=flat&color=yellow" alt="Stars"></a>
</p>

</div>

### Support the Project

<p>
  <a href="https://opencollective.com/sportarr"><img src="https://img.shields.io/badge/sponsor-Open%20Collective-7FADF2?style=flat&logo=opencollective&logoColor=white" alt="Sponsor"></a>
  <a href="https://ko-fi.com/sportarr"><img src="https://img.shields.io/badge/buy%20me%20a%20coffee-FF5E5B?style=flat&logo=ko-fi&logoColor=white" alt="Ko-fi"></a>
  <a href="https://sportarr.net/donate/btc"><img src="https://img.shields.io/badge/send-Bitcoin-F7931A?style=flat&logo=bitcoin&logoColor=white" alt="Bitcoin"></a>
</p>

---

![Sportarr Dashboard](docs/images/dashboard.png)

## What It Does

- Tracks events across all major sports (fighting sports, football, soccer, basketball, racing, etc.)
- Searches Usenet and torrent indexers automatically and upgrades quality when better releases appear
- Organizes files with customizable naming schemes, including multi-part events for fighting sports
- Integrates with Plex, Jellyfin, and Emby through dedicated metadata agents
- Records live events from IPTV sources with automatic scheduling (alpha)
- Fetches subtitles through Bazarr and notifies via Discord, ntfy, Apprise, webhooks, or custom scripts

## Quick Start

```yaml
version: "3.8"
services:
  sportarr:
    image: sportarr/sportarr:latest
    container_name: sportarr
    environment:
      - PUID=99
      - PGID=100
      - UMASK=022
      - TZ=America/New_York
    volumes:
      - /path/to/sportarr/config:/config
      - /path/to/sports:/data
    ports:
      - 1867:1867
    restart: unless-stopped
```

Open `http://your-server-ip:1867` and follow the [Initial Setup guide](https://wiki.sportarr.net/getting-started/initial-setup/).

Not on Docker? Native builds for Windows, macOS, and Linux are on the [releases page](https://github.com/Sportarr/Sportarr/releases/latest), and Sportarr is in the TrueNAS SCALE, HexOS, and Unraid app catalogs. Full details in the [installation guide](https://wiki.sportarr.net/getting-started/installation/).

## Documentation

Everything lives on the wiki at **[wiki.sportarr.net](https://wiki.sportarr.net)**:

- [Installation](https://wiki.sportarr.net/getting-started/installation/) and [Initial Setup](https://wiki.sportarr.net/getting-started/initial-setup/)
- [Integrations](https://wiki.sportarr.net/integrations/prowlarr/): Prowlarr, Bazarr, Maintainerr, Homepage, autobrr, and the media server agents
- [IPTV DVR recording](https://wiki.sportarr.net/features/iptv-dvr/)
- [Troubleshooting](https://wiki.sportarr.net/troubleshooting/)
- [Application API](https://wiki.sportarr.net/APPLICATION_API/) for tools integrating with Sportarr, and the [live metadata API explorer](https://wiki.sportarr.net/api-explorer/)
- [Building from source](https://wiki.sportarr.net/development/building/)

## Support

- [Discord](https://discord.gg/YjHVWGWjjG) - best place for quick help
- [GitHub Issues](https://github.com/Sportarr/Sportarr/issues) - bug reports and feature requests
- [GitHub Discussions](https://github.com/Sportarr/Sportarr/discussions) - general questions

## Project Activity

![Repobeats analytics](https://repobeats.axiom.co/api/embed/e53905c36a9f4ad733f63ffa19201d63ab43c890.svg)

## Star History

<a href="https://www.star-history.com/?repos=Sportarr%2FSportarr&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=Sportarr/Sportarr&type=date&theme=dark&legend=top-left&sealed_token=loM7FRYSXaXqn9lTcTishVtPaTcjxXer6HbzwSQ3Dg7QDKXGmTOsc2xoEc01aDr8vEZFTXyRc76fxgdckzJKpxH84MmDWLWNUY83aEa7xcK5XjOUWMvzqd353S9mqCK4hQ3R7kdhDXZBpI-gBTpD8cQCUEzOq-hFCBEVqXOUz9S9tuaN677TBZ8q0QT2" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=Sportarr/Sportarr&type=date&legend=top-left&sealed_token=loM7FRYSXaXqn9lTcTishVtPaTcjxXer6HbzwSQ3Dg7QDKXGmTOsc2xoEc01aDr8vEZFTXyRc76fxgdckzJKpxH84MmDWLWNUY83aEa7xcK5XjOUWMvzqd353S9mqCK4hQ3R7kdhDXZBpI-gBTpD8cQCUEzOq-hFCBEVqXOUz9S9tuaN677TBZ8q0QT2" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=Sportarr/Sportarr&type=date&legend=top-left&sealed_token=loM7FRYSXaXqn9lTcTishVtPaTcjxXer6HbzwSQ3Dg7QDKXGmTOsc2xoEc01aDr8vEZFTXyRc76fxgdckzJKpxH84MmDWLWNUY83aEa7xcK5XjOUWMvzqd353S9mqCK4hQ3R7kdhDXZBpI-gBTpD8cQCUEzOq-hFCBEVqXOUz9S9tuaN677TBZ8q0QT2" />
 </picture>
</a>

## Contributors

Sportarr is made better by everyone who has contributed code. Thank you.

<!-- Regenerated automatically by .github/workflows/contributors.yml. Lists
     code contributors (merged PRs) only; bots and the release account are
     excluded. Do not hand-edit the block between the markers. -->
<!-- readme: contributors,claude/-,Sportarr/- -start -->
<table>
	<tbody>
		<tr>
            <td align="center">
                <a href="https://github.com/BenjaminDecreusefond">
                    <img src="https://avatars.githubusercontent.com/u/180167280?v=4" width="72;" alt="BenjaminDecreusefond"/>
                    <br />
                    <sub><b>BenjaminDecreusefond</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/ohathar">
                    <img src="https://avatars.githubusercontent.com/u/6678917?v=4" width="72;" alt="ohathar"/>
                    <br />
                    <sub><b>ohathar</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/mmmmmtasty">
                    <img src="https://avatars.githubusercontent.com/u/14114638?v=4" width="72;" alt="mmmmmtasty"/>
                    <br />
                    <sub><b>mmmmmtasty</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/FacePlant101">
                    <img src="https://avatars.githubusercontent.com/u/3405597?v=4" width="72;" alt="FacePlant101"/>
                    <br />
                    <sub><b>FacePlant101</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/gwyden">
                    <img src="https://avatars.githubusercontent.com/u/7458118?v=4" width="72;" alt="gwyden"/>
                    <br />
                    <sub><b>gwyden</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/abcattell91">
                    <img src="https://avatars.githubusercontent.com/u/94864528?v=4" width="72;" alt="abcattell91"/>
                    <br />
                    <sub><b>abcattell91</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/gerrewsb">
                    <img src="https://avatars.githubusercontent.com/u/23342425?v=4" width="72;" alt="gerrewsb"/>
                    <br />
                    <sub><b>gerrewsb</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/scottrobertson">
                    <img src="https://avatars.githubusercontent.com/u/68361?v=4" width="72;" alt="scottrobertson"/>
                    <br />
                    <sub><b>scottrobertson</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/afrancke">
                    <img src="https://avatars.githubusercontent.com/u/6088682?v=4" width="72;" alt="afrancke"/>
                    <br />
                    <sub><b>afrancke</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/benjamin-decreusefond">
                    <img src="https://avatars.githubusercontent.com/u/34320855?v=4" width="72;" alt="benjamin-decreusefond"/>
                    <br />
                    <sub><b>benjamin-decreusefond</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/Donai82">
                    <img src="https://avatars.githubusercontent.com/u/99044513?v=4" width="72;" alt="Donai82"/>
                    <br />
                    <sub><b>Donai82</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/gilesw">
                    <img src="https://avatars.githubusercontent.com/u/201443?v=4" width="72;" alt="gilesw"/>
                    <br />
                    <sub><b>gilesw</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/hobbithau5">
                    <img src="https://avatars.githubusercontent.com/u/73753815?v=4" width="72;" alt="hobbithau5"/>
                    <br />
                    <sub><b>hobbithau5</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/jpaull-nz">
                    <img src="https://avatars.githubusercontent.com/u/209406876?v=4" width="72;" alt="jpaull-nz"/>
                    <br />
                    <sub><b>jpaull-nz</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/kristofferR">
                    <img src="https://avatars.githubusercontent.com/u/481270?v=4" width="72;" alt="kristofferR"/>
                    <br />
                    <sub><b>kristofferR</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/lustered">
                    <img src="https://avatars.githubusercontent.com/u/45863485?v=4" width="72;" alt="lustered"/>
                    <br />
                    <sub><b>lustered</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/lyrova-andy">
                    <img src="https://avatars.githubusercontent.com/u/277908091?v=4" width="72;" alt="lyrova-andy"/>
                    <br />
                    <sub><b>lyrova-andy</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/nathanjcollins">
                    <img src="https://avatars.githubusercontent.com/u/53304818?v=4" width="72;" alt="nathanjcollins"/>
                    <br />
                    <sub><b>nathanjcollins</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/nickperkins">
                    <img src="https://avatars.githubusercontent.com/u/569924?v=4" width="72;" alt="nickperkins"/>
                    <br />
                    <sub><b>nickperkins</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/Percentnineteen">
                    <img src="https://avatars.githubusercontent.com/u/12090180?v=4" width="72;" alt="Percentnineteen"/>
                    <br />
                    <sub><b>Percentnineteen</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/Pukabyte">
                    <img src="https://avatars.githubusercontent.com/u/120460627?v=4" width="72;" alt="Pukabyte"/>
                    <br />
                    <sub><b>Pukabyte</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/schlort">
                    <img src="https://avatars.githubusercontent.com/u/6138053?v=4" width="72;" alt="schlort"/>
                    <br />
                    <sub><b>schlort</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/skjaere">
                    <img src="https://avatars.githubusercontent.com/u/183823742?v=4" width="72;" alt="skjaere"/>
                    <br />
                    <sub><b>skjaere</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/slflowfoon">
                    <img src="https://avatars.githubusercontent.com/u/94804320?v=4" width="72;" alt="slflowfoon"/>
                    <br />
                    <sub><b>slflowfoon</b></sub>
                </a>
            </td>
		</tr>
	</tbody>
</table>
<!-- readme: contributors,claude/-,Sportarr/- -end -->

<sub>See the full <a href="https://github.com/Sportarr/Sportarr/graphs/contributors">contributor graph</a>, plus everyone helping with testing and bug reports on <a href="https://discord.gg/YjHVWGWjjG">Discord</a>.</sub>

## Sponsors

Sportarr is free and self-funded. If it saves you time, a [one-time or monthly contribution](https://opencollective.com/sportarr) keeps it moving, and every supporter shows up here.

<a href="https://opencollective.com/sportarr"><img src="https://opencollective.com/sportarr/backers.svg?width=800" alt="Backers" /></a>

<a href="https://opencollective.com/sportarr"><img src="https://opencollective.com/sportarr/sponsors.svg?width=800" alt="Sponsors" /></a>

## License

GNU GPL v3 - see [LICENSE.md](LICENSE.md)

---

Sportarr is based on Sonarr. Thanks to the Sonarr team for the foundation.
