# Library Import

Library Import brings video files you already have into Sportarr. Open it from **Library > Import**. Under **Settings > Media Management**, each root folder also lists its subfolders that are not linked to a league, with a **Library Import** link beside each one.

## Scan a folder

Click **Browse**, pick a folder, and click **Scan Folder**. **Include subfolders** is on by default, so Sportarr reads every folder below the one you picked. Turn it off to scan only the videos directly in that folder.

The results show how many files Sportarr found, matched, left unmatched, and found already in your library. The files are listed under **Matched Files**, **Unmatched Files**, and **Already in Library**.

## How Sportarr reads your folders

Sportarr reads the league and the season from the names of your folders. A library laid out like this works best.

```
/data/sports/Formula 1/Season 2024/Formula 1 - 2024 Bahrain Grand Prix - Race.mkv
/data/sports/Formula 1/Season 2024/Bahrain Grand Prix/Formula 1 - 2024 Bahrain Grand Prix - Qualifying.mkv
/data/sports/NBA/Season 2024-2025/NBA - 2024-10-22 - Knicks vs Celtics.mkv
```

The first folder whose name is a season is the season folder. The folder above it is the league folder. Without a season folder, the first folder below the root folder is the league folder. A folder below the season folder, such as an event folder, changes neither. Sportarr does not use the event folder's name for matching. The file's own name must name the event, or a metadata file beside it must give the title, as [Metadata files beside a video](#metadata-files-beside-a-video) explains.

Sportarr reads these season folder names. Capital letters do not matter.

| Folder name | Season |
|---|---|
| `Season 2024` or `2024` | 2024 |
| `Season 2024-2025` | 2024-2025 |
| `Season 2024-25` or `2024-25` | 2024-2025 |

A single year also fits a league whose seasons cross two years. When a league has no season named `2024`, a `Season 2024` folder finds the season that starts in 2024.

`Season 0` and `Specials` are not season folders. Their files are matched against all of the league's events.

A root folder is read as a league only when its own name names one of your leagues, by the rules in [How a folder names a league](#how-a-folder-names-a-league). A root folder named `Formula 1` is then the Formula 1 folder, and each folder below it is a season or event folder. Any other root folder is never read as a league, and files directly in it get no league from their folders. When you scan a folder inside a root folder, Sportarr still reads the path from the root folder down. So a scan of `Formula 1/Season 2024` alone still finds the league and the season.

Sportarr reads folders only inside your root folders. When you scan a folder outside them, such as a downloads folder, its files get no league or season from their folders and are matched against your whole library. A season folder outside them gets no league either, even when the folder above it names one. Folders outside your root folders are not listed under **Folders Without a League**.

### How a folder names a league

A league folder is linked to a league in your library only when its name is that league's name or one of its alternate names. Capital letters, spaces, dots, and hyphens do not count, so `formula.1` names Formula 1. For a league named with one word and a number, Sportarr also reads the first letter and the number, so `F1` names Formula 1. A few soccer leagues have short forms built in, such as `WSL` for the English Womens Super League. Any other short form works only when it is one of the league's alternate names.

A folder named with only part of a league's name is not linked to that league. `Premier League` does not name the Antigua and Barbuda Premier League. When a folder's name names two leagues in your library, the folder is not linked to either. It is listed under **Folders Without a League** with the folders that are not one of your leagues. A file in a folder that is not linked is still matched against your whole library.

When the folder names a league, Sportarr looks for each file's event in that league only, and in the season that the season folder names. The folder wins over a league name in the file name. When the league has no season that fits the season folder, Sportarr looks through all of that league's events.

## Folders for a league you do not have

**Folders Without a League** lists each league folder inside a root folder that is not one of your leagues. Each row says `Folder "Name" is not one of your leagues.` and shows the folder's full path.

Sportarr asks the metadata service for a league with that exact name. When it finds one, the row shows a button with the league's name, such as **Add Formula 1**. Click it to add the league. Sportarr binds the league to the root folder that holds the folder and gives it that root folder's default quality profile. It does not search for missing events.

- A league without teams, such as a motorsport series or a golf tour, is monitored for future events. So is a promotion such as UFC or WWE.
- Any other league, such as a team league, is added with no teams, so it has no events yet. Choose its teams on the league page, then scan again.

Sportarr then scans the same folder again. A new league's events can take a few minutes to load. Scan again if its files stay unmatched. If the league cannot be added, the row shows why.

When the metadata service has no league with that exact name, the row says "No matching league found. Add the league from the Leagues page, then scan again." Add the league from **Library > Add League**. If your folder uses a name of its own, rename it to the league's name so that the folder links to the league.

The row shows the same text when the metadata service does not answer in time. Sportarr waits up to 5 seconds for each answer and up to 15 seconds in all for one scan. After one slow or failed answer, Sportarr stops asking for the rest of that scan, so the rows after it show that text too. Scan again to retry.

## Files named by another database

Some libraries were named by another database such as TheTVDB, with TV-style names like `Formula 1 - S2024E05 - Chinese Grand Prix.mkv`. The season and episode numbers in those names come from that database. They often differ from Sportarr's own numbers, so Sportarr matches these files on their titles and dates.

An S/E number counts only when something else about the file agrees with the event. That can be the title, the round, the location, or the event's own day. A date a few days away does not count. When two or more events that could match the file fall on the file's date, the number does not change the score of any of them.

| The file's episode number | Something else agrees | Effect on the score |
|---|---|---|
| Same as the event's | Yes | Raises it |
| Same as the event's | No | None |
| Different | Yes | None |
| Different | No | Lowers it |

For an event that is one session of a race weekend, the round or the location counts only when the file also names that session, such as Qualifying.

A file that names one session is never matched to another session. In a series with one event for each Grand Prix, such as MotoGP, an event that names no session is the race. So a file that names practice, qualifying, a sprint, a warm-up, or a test day is not matched to it. A test-day file is also not matched to an event that names another session. A file of a show about the weekend, such as a "Best of", a press conference, a talk show, or "After The Flag", is not matched to any session or race.

When several events fit a file equally and its name points to none of them, the file stays unmatched. Choose its event yourself.

## Files Sportarr named

A file with a Sportarr id in its name, such as `sportarr-ev-2338110`, matches its event exactly. So does a file that carries the id in its embedded tags. These files match at 100, even when the event already has a file. When no event in your library has that id, Sportarr matches the file the usual way. See [The Sportarr id token](file-naming.md#the-sportarr-id-token).

A match on title and date looks only at events that have no file yet. Within one scan, it gives each event to one file only. The parts of one event, such as a fight card's prelims and main card, are the exception.

## Metadata files beside a video

When a `.nfo` file with the same name sits beside a video, Sportarr reads it. For `Chinese Grand Prix.mkv`, that is `Chinese Grand Prix.nfo`. Sportarr reads three things from it.

- The air date, from `aired`, or from `premiered` when there is no `aired`
- The title, from `title`
- A Sportarr id, from a `uniqueid` with the type `sportarr`

The file name comes first. The metadata file's date is used only when the name has no date. Its title is used only when the name has almost no title of its own. A Sportarr id in it matches the event exactly, the same way an id in the file name does.

Sportarr reads only episode and movie metadata files, whose top element is `episodedetails` or `movie`. It skips a file larger than 1 MB and a file it cannot read.

Reading the metadata file never changes it, and a scan never moves or deletes it. If **Episode NFO** is on under **Settings > Local Metadata**, an import writes Sportarr's own `.nfo` beside the imported video. That replaces any `.nfo` with the same name in that place.

## Match confidence

Each matched file shows a score.

| Color | Score |
|---|---|
| Green | 80 or higher |
| Yellow | 60 to 79 |
| Orange | 40 to 59 |

A file that scores under 40 is not matched. It is listed under **Unmatched Files**. The score goes away after you choose a file's event yourself.

With **Send Anonymous Usage Data** on, a file can also match at 90 to the event that other installs matched it to, as [Anonymous Usage Data](anonymous-usage-data.md#matches-other-installs-agreed-on) explains.

## Choose an event yourself

Click **Match** on an unmatched file, or **Edit** on a matched one, to choose the league, season, event, and part. When an unmatched file's folder names one of your leagues, the dialog starts on that league and season and says "Chosen from the file's folder."

## Import the files

**Select Matched Only** selects every file under **Matched Files** and **Already in Library**. Then click **Import** to import each file to its suggested event. Files under **Already in Library** are imported again to their correct place and name. Check the list before you import, and clear any file you are not sure of. A yellow or orange score is a good reason to look.

**Select All** also selects the unmatched files. An unmatched file that you import without choosing an event becomes a new event, named from the file.

When an event already has a file, the row says why the new file would not replace it. The rules are in [A file for an event that already has one](download-clients.md#a-file-for-an-event-that-already-has-one). An IPTV recording held by an indexer file is not selected. Select **Import anyway** on that file if you want the recording to replace the indexer file.

When **Activity** also lists a file that you import here, the file leaves **Activity**.

**Import Mode** decides what happens to the original files. **Auto** follows your Media Management settings. **Hardlink/Copy** and **Copy** leave the originals untouched, so seeding torrents stay intact. **Hardlink/Copy** links when the source and the library are on the same filesystem and copies otherwise. **Move** relocates the files and frees the source.

## Background scans

Two other scans read your library. **Rescan library** on **System > Backup** walks every root folder. The file watcher checks each new file it sees in a root folder. Both read your folders and metadata files the same way Library Import does. They never ask the metadata service about a folder.

| Scan | Reads folders | Reads metadata files | Asks the metadata service |
|---|---|---|---|
| Library Import | Yes | Yes | Yes |
| Rescan library and the file watcher | Yes | Yes | No |
| Completed downloads | No | No | No |

The rescan and the file watcher import a file on their own only when it scores 85 or higher. The file watcher lists a lower-scoring file in **Activity** for review. The rescan leaves it where it is. Scan its folder from Library Import to review it.

When the event already has a file, the rescan replaces that file only with an upgrade. A copy that is not an upgrade stays where it is, and **Activity** lists it with the reason.
