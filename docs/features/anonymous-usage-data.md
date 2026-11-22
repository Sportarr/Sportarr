# Anonymous Usage Data

Sportarr can send a small amount of anonymous data to sportarr.net, the service that supplies its sports data. The data helps fix team names, missing events, and file matches for everyone. This page lists every item Sportarr sends, what each one is used for, and what comes back.

The setting is **Send Anonymous Usage Data** under **Settings > General > Analytics**. It is on by default.

## Turn it off

Open **Settings > General**. Under **Analytics**, clear **Send Anonymous Usage Data**, then save.

With the setting off, Sportarr sends none of the items on this page. It also stops asking sportarr.net for matches that other installs agreed on. Sportarr still loads leagues, events, and images from sportarr.net, and still looks up leagues by name, because it needs that data to work.

## The install id

Each report carries a random install id. It holds nothing about you or your computer. Sportarr makes it the first time it sends a report and saves it in its `config.xml` file.

The service uses the id to count each install once. An install that sends the same report many times still counts as one.

## What Sportarr sends

### Team aliases

You can add your own aliases for a team from **Aliases** on a league page. When you save them, Sportarr sends the aliases, the team's name, the team's sportarr.net id, and the id of its league.

The service counts how many installs use each alias. The sportarr.net team adds popular aliases to the team's alternate names by hand. Every install then matches releases that use that name.

### Releases that match no event

An RSS sync can find a release that names one of your monitored leagues and a date, when that league has no event within a day of that date. Sportarr then sends the release title, the league's id, and the date. It sends only dates from 30 days ago to 7 days ahead. It does not send the name of the indexer.

The service counts how many installs saw each release. That shows the sportarr.net team which events are missing, so they can add them.

### Files you match

When you import a file to an event yourself, Sportarr sends these items.

- The file's name, without its folder or its extension
- A cleaned copy of the name, as described below
- The sportarr.net id of the event and of its league
- The event's season
- Whether you picked the event yourself or kept Sportarr's suggestion

To clean the name, Sportarr puts it in lower case and takes out any Sportarr id. It takes out a release group at the end of the name when a quality, source, or codec tag comes right before it, such as `GRP` in `x265-GRP`. A word after a dashed tag that names part of the event stays, such as `Sprint` in `1080p-Sprint`. Then Sportarr takes out the quality, source, codec, language, proper, repack, and edition tags, but only from the first quality or source tag on, such as `1080p` or `WEB-DL`. Title words before that tag stay. A frame rate of 24, 25, 30, 50 or 60 goes with the quality tag, such as `1080p60` or `60fps` in `1080p 60fps`. Other frame rates stay, so `1080p120` stays in the name. Each run of spaces and punctuation then becomes one space. Years and season and episode numbers stay.

This happens when you import from **Library Import**, and when you accept a file in the **Manual Import** dialog. Sportarr does not send these items in the cases below.

- An import that Sportarr makes on its own, such as a completed download, **Rescan library**, or the file watcher
- A file that Sportarr already knows by its own id, in the file's name, its embedded tags, its metadata file, or the release title
- A file you import as a new event
- A file that the event already holds, when you import it again
- An event or a league that has no sportarr.net id
- A match from other installs that you import without a change, because sportarr.net gave that answer itself
- A file that **Match season** paired with an event, because the pair comes from the order of the list
- An IPTV recording

The service counts how many installs matched each cleaned name to each event. Once a day it looks for the names that installs agree on. A name qualifies only when all of these are true.

- At least three installs matched it to the same event.
- At least one of those installs picked the event by hand.
- That event has at least twice as many installs as all other events for the name together.

A person on the sportarr.net team then reviews each name that qualifies. Only an approved match is shared with other installs.

### Releases you pick by hand

Sometimes you pick a release in an event's **Manual Search** list that Sportarr scored low, did not match, or rejected because its name or date did not fit the event. When the file from that release imports for the first time, Sportarr sends these items.

- The release title as the indexer gave it
- A cleaned copy of the title, made the same way as for files
- The sportarr.net id of the event and of its league
- The event's season
- Whether it was a low score, no match, or a rejection
- Sportarr's match score for the release
- The kind of indexer, which is Torznab, Newznab, RSS, or other
- Whether the release came by torrent or usenet
- The Sportarr version
- A flag for a hint from other installs, which is always off in this version

Sportarr never sends the indexer's name, address, or key, the download link, or the size, seeders, or age of the release. It never sends a file path or any download client detail, your quality profile or format names, the search text you typed, or the time of the grab.

Sportarr does not send these items in the cases below.

- An automatic grab, an RSS grab, or a search you start with the **Search** button, because Sportarr picked those releases itself
- A season or week pack
- A release you grab from a season search on a league page, because Sportarr matched that release to the events
- A release that Sportarr already matched well
- A title with no year
- A title with a Sportarr id
- A title that holds a web address or a slash
- A title that starts with a tag in brackets, such as `[TGx]`, because such a tag often names a site
- A title longer than 300 characters
- An event or a league that has no sportarr.net id
- A file that never imports, or a file from the same grab that imports again

The service counts how many installs picked each cleaned title for each event. The sportarr.net team uses that list to fix matching and to add league and team names by hand. Nothing from this report comes back to any install.

## What Sportarr receives

### Matches other installs agreed on

When you scan a folder on the **Library Import** page, Sportarr sends the cleaned names of the files in the folder. It leaves out files already in your library, files you ignored, and files that carry a Sportarr event id in their name or their metadata file. Library Import still lists the files you ignored, but it does not send their names. Sportarr does not read embedded tags first, so a file that it later matches by the id in its tags can still be looked up. This request does not carry the install id.

The service answers with the event for each name that has an approved match. Sportarr uses that event only when all of these are true.

- The event is in your library.
- The event is in the league that the file's folder names, if the folder names one.
- The event fits the season folder, the year the file gives, and the session the file names.
- The file's name, its folders, or its metadata file give a year. A name with no year is the same every year, so its match could be the event of another year.
- No other file in the same scan already took that event.

A file matched this way shows a score of 90. You review it on the page before you import, the same as any other match. If sportarr.net does not answer within 5 seconds, the scan matches the files the usual way.

**Rescan library**, the file watcher, and completed downloads never ask for these matches.

### Team names and events

Aliases that the sportarr.net team adds, and events they add, reach every install through the normal data sync.

## What sportarr.net keeps

For each report, sportarr.net keeps the items listed above, the install id, and the dates of the first and the latest report. These records have no field for your IP address.

The service keeps the reports apart from the data it serves. Nothing from them reaches another install until a person on the sportarr.net team approves it.
