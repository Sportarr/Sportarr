# Direct database fix for SABnzbd type issue
# This script directly queries and fixes the DownloadClients table

$dbPath = "F:\Downloads\PROGRAM DOWNLOADS\Fightarr\Fightarr\src\Data\fightarr.db"

Write-Host "SABnzbd Type Fix Script" -ForegroundColor Cyan
Write-Host "======================" -ForegroundColor Cyan
Write-Host ""

# Check if db exists
if (-not (Test-Path $dbPath)) {
    Write-Host "ERROR: Database not found at $dbPath" -ForegroundColor Red
    exit 1
}

Write-Host "Database found at: $dbPath" -ForegroundColor Green
Write-Host ""

# Use System.Data.SQLite if available, otherwise use dotnet ef
try {
    # Try to load SQLite assembly
    Add-Type -Path "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Data.SQLite\v4.0_1.0.118.0__db937bc2d44ff139\System.Data.SQLite.dll" -ErrorAction Stop

    $connection = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbPath;Version=3;")
    $connection.Open()

    # Query current download clients
    Write-Host "Current Download Clients:" -ForegroundColor Yellow
    Write-Host "========================" -ForegroundColor Yellow

    $query = "SELECT Id, Name, Type, Host, Port FROM DownloadClients;"
    $command = $connection.CreateCommand()
    $command.CommandText = $query
    $reader = $command.ExecuteReader()

    while ($reader.Read()) {
        $id = $reader["Id"]
        $name = $reader["Name"]
        $type = $reader["Type"]
        $host = $reader["Host"]
        $port = $reader["Port"]

        $typeName = switch ($type) {
            0 { "qBittorrent (TORRENT)" }
            1 { "Transmission (TORRENT)" }
            2 { "Deluge (TORRENT)" }
            3 { "rTorrent (TORRENT)" }
            4 { "uTorrent (TORRENT)" }
            5 { "SABnzbd (USENET)" }
            6 { "NZBGet (USENET)" }
            default { "Unknown ($type)" }
        }

        Write-Host "ID: $id | Name: $name | Type: $type ($typeName) | $host:$port"
    }
    $reader.Close()

    Write-Host ""
    Write-Host "Fixing SABnzbd clients..." -ForegroundColor Cyan

    # Fix Type=4 to Type=5 (UTorrent -> SABnzbd)
    $updateQuery1 = "UPDATE DownloadClients SET Type = 5 WHERE Type = 4;"
    $updateCommand1 = $connection.CreateCommand()
    $updateCommand1.CommandText = $updateQuery1
    $rows1 = $updateCommand1.ExecuteNonQuery()
    Write-Host "Fixed $rows1 clients from Type 4 to Type 5 (SABnzbd)" -ForegroundColor Green

    # Fix Type=6 to Type=5 if port is not 6789 (NZBGet -> SABnzbd)
    $updateQuery2 = "UPDATE DownloadClients SET Type = 5 WHERE Type = 6 AND Port != 6789;"
    $updateCommand2 = $connection.CreateCommand()
    $updateCommand2.CommandText = $updateQuery2
    $rows2 = $updateCommand2.ExecuteNonQuery()
    Write-Host "Fixed $rows2 clients from Type 6 to Type 5 (NZBGet misidentified as SABnzbd)" -ForegroundColor Green

    Write-Host ""
    Write-Host "Updated Download Clients:" -ForegroundColor Yellow
    Write-Host "========================" -ForegroundColor Yellow

    $query2 = "SELECT Id, Name, Type, Host, Port FROM DownloadClients;"
    $command2 = $connection.CreateCommand()
    $command2.CommandText = $query2
    $reader2 = $command2.ExecuteReader()

    while ($reader2.Read()) {
        $id = $reader2["Id"]
        $name = $reader2["Name"]
        $type = $reader2["Type"]
        $host = $reader2["Host"]
        $port = $reader2["Port"]

        $typeName = switch ($type) {
            0 { "qBittorrent (TORRENT)" }
            1 { "Transmission (TORRENT)" }
            2 { "Deluge (TORRENT)" }
            3 { "rTorrent (TORRENT)" }
            4 { "uTorrent (TORRENT)" }
            5 { "SABnzbd (USENET)" }
            6 { "NZBGet (USENET)" }
            default { "Unknown ($type)" }
        }

        Write-Host "ID: $id | Name: $name | Type: $type ($typeName) | $host:$port"
    }
    $reader2.Close()

    $connection.Close()

    Write-Host ""
    Write-Host "Fix completed successfully! Restart Fightarr to see changes." -ForegroundColor Green

} catch {
    Write-Host "SQLite library not available. Using alternative method..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Please run the following commands manually:" -ForegroundColor Cyan
    Write-Host "cd 'F:\Downloads\PROGRAM DOWNLOADS\Fightarr\Fightarr'" -ForegroundColor White
    Write-Host "dotnet ef database update --project src/Fightarr.Api.csproj" -ForegroundColor White
    Write-Host ""
    Write-Host "Error details: $_" -ForegroundColor Red
}
