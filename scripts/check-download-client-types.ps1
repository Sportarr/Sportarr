# Diagnostic script to check download client enum values in database
# Run this to verify that SABnzbd clients have the correct Type value (5)

$dbPath = "$PSScriptRoot\..\src\bin\Debug\net9.0\Fightarr.db"

if (-not (Test-Path $dbPath)) {
    Write-Host "Database not found at: $dbPath" -ForegroundColor Red
    Write-Host "Looking for database in other common locations..." -ForegroundColor Yellow

    $alternativePaths = @(
        "$PSScriptRoot\..\src\bin\Release\net9.0\Fightarr.db",
        "$PSScriptRoot\..\src\Fightarr.db",
        "$PSScriptRoot\..\Fightarr.db"
    )

    foreach ($path in $alternativePaths) {
        if (Test-Path $path) {
            $dbPath = $path
            Write-Host "Found database at: $dbPath" -ForegroundColor Green
            break
        }
    }

    if (-not (Test-Path $dbPath)) {
        Write-Host "Could not find Fightarr.db in any common location." -ForegroundColor Red
        Write-Host "Please specify the path manually or run Fightarr once to create the database." -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "`nQuerying download clients from database..." -ForegroundColor Cyan
Write-Host "Database: $dbPath`n" -ForegroundColor Gray

# Query using sqlite3 command if available
$result = & sqlite3 $dbPath "SELECT Id, Name, Type, Host, Port, Enabled FROM DownloadClients;" 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "Download Clients:" -ForegroundColor Green
    Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
    Write-Host "ID | Name                 | Type | Host          | Port | Enabled" -ForegroundColor Yellow
    Write-Host "────────────────────────────────────────────────────────────────────" -ForegroundColor Gray

    if ($result) {
        $result | ForEach-Object {
            Write-Host $_ -ForegroundColor White
        }
    } else {
        Write-Host "No download clients found in database." -ForegroundColor Yellow
    }

    Write-Host "`n"
    Write-Host "Enum Reference:" -ForegroundColor Cyan
    Write-Host "─────────────────" -ForegroundColor Gray
    Write-Host "0 = QBittorrent (Torrent)" -ForegroundColor Green
    Write-Host "1 = Transmission (Torrent)" -ForegroundColor Green
    Write-Host "2 = Deluge (Torrent)" -ForegroundColor Green
    Write-Host "3 = RTorrent (Torrent)" -ForegroundColor Green
    Write-Host "4 = UTorrent (Torrent)" -ForegroundColor Green
    Write-Host "5 = SABnzbd (Usenet)" -ForegroundColor Cyan
    Write-Host "6 = NZBGet (Usenet)" -ForegroundColor Cyan

    Write-Host "`n"
    Write-Host "If any SABnzbd client shows Type=4, that's the bug!" -ForegroundColor Yellow
    Write-Host "SABnzbd should be Type=5, NZBGet should be Type=6" -ForegroundColor Yellow
} else {
    Write-Host "Error: sqlite3 command not found or failed to execute." -ForegroundColor Red
    Write-Host "Please install sqlite3 or check the database manually." -ForegroundColor Yellow
    Write-Host "`nAlternatively, you can check using any SQLite browser:" -ForegroundColor Gray
    Write-Host "SELECT * FROM DownloadClients;" -ForegroundColor White
}
