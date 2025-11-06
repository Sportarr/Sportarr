-- FIX SCRIPT: Update SABnzbd download clients from Type=4 to Type=5
-- Run this against your Fightarr.db to fix the enum value

-- First, let's see what we have
SELECT 'BEFORE FIX:' as Stage, Id, Name, Type, Host, Port FROM DownloadClients;

-- Update any SABnzbd clients that have Type=4 (UTorrent) to Type=5 (SABnzbd)
-- We identify them by name containing 'SAB' or 'sabnzbd'
UPDATE DownloadClients
SET Type = 5
WHERE (Name LIKE '%SAB%' OR Name LIKE '%sabnzbd%')
  AND Type = 4;

-- Update any NZBGet clients that have Type=5 to Type=6
-- (They would have been shifted if added with the old bug)
UPDATE DownloadClients
SET Type = 6
WHERE (Name LIKE '%NZB%' OR Name LIKE '%nzbget%')
  AND Type = 5
  AND NOT (Name LIKE '%SAB%');

-- Show the results
SELECT 'AFTER FIX:' as Stage, Id, Name, Type, Host, Port FROM DownloadClients;

-- Verification query
SELECT
  Name,
  Type,
  CASE
    WHEN Type = 0 THEN 'QBittorrent (Torrent)'
    WHEN Type = 1 THEN 'Transmission (Torrent)'
    WHEN Type = 2 THEN 'Deluge (Torrent)'
    WHEN Type = 3 THEN 'RTorrent (Torrent)'
    WHEN Type = 4 THEN 'UTorrent (Torrent)'
    WHEN Type = 5 THEN 'SABnzbd (Usenet)'
    WHEN Type = 6 THEN 'NZBGet (Usenet)'
    ELSE 'UNKNOWN'
  END as TypeName
FROM DownloadClients;
