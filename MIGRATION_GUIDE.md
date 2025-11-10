# Migration Guide: Universal Sports Transition

This guide helps existing Sportarr users transition from the combat sports-focused version to the new universal sports version that supports all major sports.

## What's Changed?

Sportarr has evolved from a UFC/MMA-focused PVR to a **universal sports PVR** supporting 11 major sport categories including Fighting, Soccer, Basketball, American Football, Baseball, Ice Hockey, Tennis, Golf, Motorsport, Rugby, and Cricket.

### Version 4.0.249+ Changes

#### Database Schema Updates

The following fields have been renamed for universal sports support:

| Old Field Name | New Field Name | Location |
|----------------|----------------|----------|
| `OrganizationFilter` | `LeagueFilter` | Import Lists |
| `FightCardNfo` | `EventCardNfo` | Metadata Providers |
| `FighterImages` | `PlayerImages` | Metadata Providers |
| `OrganizationLogos` | `LeagueLogos` | Metadata Providers |

**These changes are applied automatically during the upgrade** - no manual action required.

#### Terminology Changes

- **Organization** → **League** (e.g., UFC, NFL, NBA, Premier League)
- **Fight Card** → **Event Card** (for card-style events)
- **Fighter** → **Player** / **Athlete** (depending on context)

## Automatic Migration

When you upgrade to version 4.0.249 or later, Sportarr will automatically:

1. ✅ Run database migrations on startup
2. ✅ Rename database columns to use universal terminology
3. ✅ Preserve all your existing data (events, import lists, settings)
4. ✅ Continue monitoring your existing Fighting/MMA events

**No data will be lost** during this migration.

## Backup Recommendation

While the migration is automatic and safe, we recommend backing up your database before upgrading:

### Docker Users

```bash
# Stop Sportarr
docker stop sportarr

# Backup the database
cp /path/to/config/sportarr.db /path/to/backup/sportarr.db.backup

# Start Sportarr (migration will run automatically)
docker start sportarr
```

### Manual Installation Users

```bash
# Stop Sportarr service/application

# Backup the database (location varies by OS)
# Windows: %AppData%\Sportarr\sportarr.db
# Linux: ~/.config/Sportarr/sportarr.db
# macOS: ~/.config/Sportarr/sportarr.db

# Start Sportarr
```

## What to Expect After Upgrade

### Import Lists

Your existing import lists will continue to work exactly as before. The only change is the filter field name:

- **Before**: "Organization Filter" field
- **After**: "League Filter" field (same functionality, new name)

### Metadata Providers

If you have metadata providers configured (for Kodi, Plex, etc.), they will continue generating NFO files and downloading images with the new field names. No reconfiguration needed.

### Existing Events

All your existing UFC/MMA events will:
- ✅ Remain in your library
- ✅ Continue to be monitored
- ✅ Be categorized as "Fighting" sport
- ✅ Work with all existing features

### Sport Detection

New events imported from version 4.0.249+ will be automatically categorized by sport:

- **Fighting**: UFC, Bellator, ONE FC, PFL, Boxing, etc.
- **Soccer**: Premier League, La Liga, Champions League, etc.
- **Basketball**: NBA, FIBA, EuroLeague, etc.
- **American Football**: NFL, NCAA Football, CFL, etc.
- **Baseball**: MLB, NPB, KBO, etc.
- **Ice Hockey**: NHL, KHL, SHL, etc.
- **Tennis**: Grand Slams, ATP, WTA, etc.
- **Golf**: PGA, Masters, Ryder Cup, etc.
- **Motorsport**: Formula 1, NASCAR, MotoGP, etc.
- **Rugby**: Six Nations, Rugby World Cup, NRL, etc.
- **Cricket**: IPL, BBL, Test Matches, T20, etc.

Sport detection is based on keywords in league names and event titles. Events that don't match any sport-specific keywords default to "Fighting" for backward compatibility.

## Rollback Instructions

If you need to rollback to a previous version:

### Docker Users

```bash
# Stop current version
docker stop sportarr

# Restore backup database
cp /path/to/backup/sportarr.db.backup /path/to/config/sportarr.db

# Use previous Docker image version
docker run -d \
  --name=sportarr \
  # ... your existing config ...
  sportarr/sportarr:4.0.248  # or your previous version
```

### Manual Installation Users

1. Stop Sportarr
2. Restore the backup database
3. Install the previous version from [GitHub Releases](https://github.com/Sportarr/Sportarr/releases)

## Migration Verification

After upgrading, verify the migration was successful:

1. **Check System Status**
   - Navigate to Settings → System
   - Verify version shows 4.0.249 or higher

2. **Check Database Schema**
   - Go to Settings → Import Lists
   - Confirm you see "League Filter" instead of "Organization Filter"

3. **Check Existing Events**
   - Go to Events page
   - Verify all your existing events are still present
   - Existing events should show "Fighting" as their sport

4. **Check Logs**
   - Go to System → Logs
   - Look for migration success message: "Database migrations completed successfully"

## Troubleshooting

### Migration Failed

If you see an error like "Database migration failed" in the logs:

1. **Check Database File Permissions**
   - Ensure Sportarr has read/write access to the database file
   - Docker: Verify PUID/PGID match your user

2. **Check Database Integrity**
   ```bash
   # If using SQLite (default)
   sqlite3 /path/to/sportarr.db "PRAGMA integrity_check;"
   ```

3. **Restore Backup and Retry**
   - Restore your backup database
   - Restart Sportarr to retry the migration

4. **Get Help**
   - Join our [Discord Server](https://discord.gg/YjHVWGWjjG)
   - Create a [GitHub Issue](https://github.com/Sportarr/Sportarr/issues) with logs

### Events Not Showing Correct Sport

If new events are being categorized as "Fighting" when they should be a different sport:

1. **Check Event Title/League Name**
   - Sport detection uses keywords from organization names and event titles
   - Ensure the event has clear sport indicators (e.g., "NFL" for American Football)

2. **Manual Override**
   - Edit the event
   - Manually change the Sport field to the correct category

3. **Report Issue**
   - If a legitimate event is miscategorized, please report it on GitHub
   - Include the league name and event title so we can improve detection

## Frequently Asked Questions

### Will my existing import lists stop working?

No. Import lists continue to function exactly as before. The "Organization Filter" field has been renamed to "League Filter" but works identically.

### Do I need to reconfigure my download clients?

No. Download client configurations are unchanged.

### Will my file naming patterns break?

No. All existing file naming tokens (like `{Event Title}`, `{Organization}`, etc.) continue to work. The `{Organization}` token now maps to the League name.

### Can I still use Sportarr for just UFC/MMA?

Absolutely! If you only want UFC/MMA events, simply:
- Continue using your existing import lists
- All UFC/MMA events are categorized as "Fighting" sport
- No changes needed to your workflow

### How do I add non-fighting sports?

After upgrading:
1. Go to **Settings → Import Lists**
2. Add a new import list for your desired sport (e.g., an RSS feed for NBA games)
3. Sportarr will automatically detect and categorize the sport
4. Events will be monitored and downloaded just like UFC events

### Is TheSportsDB API required?

Sportarr uses TheSportsDB for enhanced metadata (team info, player stats, etc.) but it's not required. You can continue using RSS feeds, iCal calendars, and other import list sources.

## Getting Help

If you encounter issues during migration:

- 💬 **Discord**: [Join our community](https://discord.gg/YjHVWGWjjG) for real-time support
- 🐛 **GitHub Issues**: [Report bugs](https://github.com/Sportarr/Sportarr/issues)
- 📖 **Documentation**: [GitHub Wiki](https://github.com/Sportarr/Sportarr/wiki)

---

**Welcome to Universal Sports Sportarr!** 🎉

We're excited to bring support for all your favorite sports. Whether you're a UFC fan, soccer enthusiast, NFL follower, or multi-sport fanatic, Sportarr now has you covered.
