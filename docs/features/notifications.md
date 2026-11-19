# Notifications

Sportarr can notify you on grabs, imports, upgrades, health issues, DVR recordings, and completed EPG syncs. Configure providers under **Settings > Notifications**:

- **Discord**, **Telegram**, **Slack**, **Mattermost**, and **Pushover**
- **Notifiarr**, sent natively to notifiarr.com - see [Notifiarr](../integrations/notifiarr.md)
- **Gotify**, **Join**, **Pushbullet**, and **SimplePush**
- **Email (SMTP)**
- Generic **webhooks**, with optional username/password auth and custom headers
- **ntfy**, self-hosted or ntfy.sh
- **Apprise**, one endpoint covering 90+ services
- **Custom scripts** that run any executable on events, with event details passed as environment variables
- **Plex**, **Jellyfin**, **Emby**, and **Kodi** library-refresh connections - see [Plex](../integrations/plex.md), [Jellyfin](../integrations/jellyfin.md), [Emby](../integrations/emby.md), and [Kodi](../integrations/kodi.md)

## Deleting a league

Deleting a league sends one notification of each kind for the league, not one for each of its events. This is true from the Sportarr UI and from `DELETE /api/v3/series/{id}`.

- **On Event File Delete** goes first, and only when the files were deleted. The webhook event type is `EpisodeFileDelete`, and `deletedFiles` lists every file that was on disk and was deleted with the league. A file that could not be deleted is still on disk, so it is not in the list. A file that was already missing, for example on a share that is offline, is not in the list either. `series.path` is the folder inside the root folder that held the files, when Sportarr can work one out. Otherwise it is the folder of the first file. When Sportarr can work out the folder, Plex, Jellyfin, Emby and Kodi rescan that folder or the nearest folder above it that still exists, in place of a full library refresh.
- **On Event Delete** goes next. The webhook event type is `SeriesDelete`. The message says how many events went, and whether files were deleted, kept on disk, or none were found to delete.

Neither payload has `series.id`, because no single event is involved. Match on `series.leagueId`, the league's id, or on `series.league`, its name. The league-level `SeriesAdd` sent when you add a league carries the same `series.leagueId`.

Both follow the league's tags, as other notifications do. They are sent after the delete is saved. A connection that is slow or fails does not stop the delete.

Custom scripts get `SPORTARR_LEAGUEID` and `SPORTARR_DELETEDFILES_COUNT`, the number of files. Only the first 100 files are passed as `SPORTARR_DELETEDFILES_{n}_PATH`, `_QUALITY` and `_SIZE`, because the process environment has a size limit. Webhooks always list every file.

## EPG sync completed

Webhook and Custom Script connections can enable **On EPG Sync Complete**. Sportarr sends one notification after an EPG source has downloaded, saved, and finished automatic channel mapping.

The webhook event type is `EpgSyncCompleted`. Its payload includes `epgSourceId`, `epgSourceName`, `channelCount`, `programCount`, `autoMappedChannelCount`, and `completedAt` in UTC.

Custom scripts receive the same values as `SPORTARR_EPGSOURCEID`, `SPORTARR_EPGSOURCENAME`, `SPORTARR_CHANNELCOUNT`, `SPORTARR_PROGRAMCOUNT`, `SPORTARR_AUTOMAPPEDCHANNELCOUNT`, and `SPORTARR_COMPLETEDAT`. `SPORTARR_EVENT_TYPE` is `OnEpgSyncCompleted`.
