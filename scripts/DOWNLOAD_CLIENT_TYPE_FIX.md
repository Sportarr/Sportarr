# SABnzbd/NZBGet Download Client Type Fix

## Issue Description

In versions prior to v4.0.188, there was a bug where the frontend enum mapping was missing `UTorrent` (value 4), causing SABnzbd and NZBGet to be mapped to incorrect enum values:

### The Bug:
- **Frontend (INCORRECT):** SABnzbd=4, NZBGet=5
- **Backend (CORRECT):** QBittorrent=0, Transmission=1, Deluge=2, RTorrent=3, **UTorrent=4**, **Sabnzbd=5**, **NzbGet=6**

### Result:
When users added a SABnzbd client through the UI:
1. Frontend sent `Type=4` to the backend
2. Backend received it as `UTorrent` (enum value 4)
3. SABnzbd clients were incorrectly treated as **torrent clients** instead of **usenet clients**
4. This caused indexer filtering to fail, preventing downloads

## Fix Details

**Fixed in:** v4.0.188 (commit 6715e73b8)
**Deployed in:** v4.0.190 (commit b6c524520)

### Changes Made:
1. Updated frontend enum mappings to include all client types
2. Corrected SABnzbd: 4 → 5
3. Corrected NZBGet: 5 → 6
4. Added uTorrent: 4
5. Fixed protocol detection: usenet now checks types 5 & 6 (was 4 & 5)

## How to Fix Affected Installations

If you added SABnzbd or NZBGet clients **before v4.0.188**, they may have incorrect `Type` values in your database.

### Symptoms:
- SABnzbd or NZBGet showing as **"TORRENT"** with green badge instead of **"USENET"** with blue badge
- Usenet indexers not being searched even though SABnzbd is enabled
- Logs showing "No matching download client available" for Newznab indexers

### Solution:

**Option 1: Manual Fix (Recommended)**
1. Update to v4.0.190 or later
2. **Clear your browser cache** (Ctrl+Shift+Delete or Cmd+Shift+Delete)
3. **Hard refresh** the Sportarr page (Ctrl+F5 or Cmd+Shift+R)
4. Go to **Settings → Download Clients**
5. **Delete** any SABnzbd/NZBGet clients showing as "TORRENT"
6. **Re-add** your SABnzbd/NZBGet clients
7. Verify they now show as **"USENET"** with blue badge

**Option 2: Diagnostic Scripts**

Run the diagnostic script to check for issues:
```powershell
.\scripts\check-download-client-types.ps1
```

This will show:
- All download clients and their Type values
- Whether any clients have suspicious Type=4 values
- Enum reference guide

**Option 3: Database Direct Fix (Advanced)**

If you're comfortable with SQL, you can directly query your database:

```powershell
sqlite3 Sportarr.db "SELECT Id, Name, Type, Host FROM DownloadClients;"
```

Look for:
- Any SABnzbd clients with `Type=4` (should be `Type=5`)
- Any NZBGet clients with `Type=5` (should be `Type=6`)

**WARNING:** Do NOT manually update the Type values in the database. The enum mapping is different between versions, and manual updates may cause other issues. Always delete and re-add the client through the UI.

## Browser Caching Issue

Even after updating to v4.0.190+, you may still see the issue due to aggressive browser caching. The frontend JavaScript is cached with content hashes in the filename (`index-DarmdC30.js`), but some browsers or CDNs may ignore these.

###To force a fresh load:

**Chrome/Edge:**
- Windows: `Ctrl + Shift + Delete` → Clear "Cached images and files" → `Ctrl + F5`
- Mac: `Cmd + Shift + Delete` → Clear cache → `Cmd + Shift + R`

**Firefox:**
- Windows: `Ctrl + Shift + Delete` → Clear "Cache" → `Ctrl + F5`
- Mac: `Cmd + Shift + Delete` → Clear cache → `Cmd + Shift + R`

**Safari:**
- `Cmd + Option + E` (empty caches) → `Cmd + R`

## Verification

After fixing, verify the issue is resolved:

1. Go to **Settings → Download Clients**
2. Check your SABnzbd/NZBGet clients
3. They should show:
   - **"USENET"** label with **blue background**
   - **NOT** "TORRENT" with green background

4. Go to **Activity → Queue** and trigger a manual search for an event
5. Check logs for `[Indexer Search] Available download clients: Torrent=False, Usenet=True`
6. Newznab indexers should now be searched

## Technical Details

### Correct Enum Mapping (v4.0.188+):

**Backend Enum** (`src/Models/Download.cs`):
```csharp
public enum DownloadClientType
{
    QBittorrent = 0,
    Transmission = 1,
    Deluge = 2,
    RTorrent = 3,
    UTorrent = 4,
    Sabnzbd = 5,
    NzbGet = 6
}
```

**Frontend Mapping** (`frontend/src/pages/settings/DownloadClientsSettings.tsx`):
```typescript
const clientTypeMap: Record<string, number> = {
  'qBittorrent': 0,
  'Transmission': 1,
  'Deluge': 2,
  'rTorrent': 3,
  'uTorrent': 4,
  'SABnzbd': 5,
  'NZBGet': 6
};
```

**Protocol Detection**:
```typescript
const getProtocol = (type: number): 'usenet' | 'torrent' => {
  return (type === 5 || type === 6) ? 'usenet' : 'torrent';
};
```

## Related Issues

- Fixed in commit: 6715e73b8 "fix: Correct download client type enum mapping for Sabnzbd and NzbGet"
- Deployed in commit: b6c524520 "fix: Deploy corrected frontend with SABnzbd/NZBGet enum fix"
- Version: v4.0.190

## Still Having Issues?

If you've updated to v4.0.190+, cleared your browser cache, deleted and re-added your SABnzbd client, and it still shows as "TORRENT":

1. Verify your version: Check bottom-left corner of Sportarr UI or GitHub releases page
2. Check browser console (F12 → Console tab) for JavaScript errors
3. Check Sportarr logs for any backend errors
4. Report the issue on GitHub with:
   - Your Sportarr version
   - Browser and version
   - Screenshots of the Download Clients page
   - Output from `check-download-client-types.ps1` script
   - Relevant log entries from Sportarr logs
