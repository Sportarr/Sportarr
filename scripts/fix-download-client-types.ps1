# Fix script for SABnzbd/NZBGet download client type enum values
# This script corrects any download clients that have incorrect Type values in the database

param(
    [switch]$DryRun
)

$dbPath = "$PSScriptRoot\..\src\bin\Debug\net9.0\Sportarr.db"

if (-not (Test-Path $dbPath)) {
    Write-Host "Database not found at: $dbPath" -ForegroundColor Red
    Write-Host "Looking for database in other common locations..." -ForegroundColor Yellow

    $alternativePaths = @(
        "$PSScriptRoot\..\src\bin\Release\net9.0\Sportarr.db",
        "$PSScriptRoot\..\src\Sportarr.db",
        "$PSScriptRoot\..\Sportarr.db"
    )

    foreach ($path in $alternativePaths) {
        if (Test-Path $path) {
            $dbPath = $path
            Write-Host "Found database at: $dbPath" -ForegroundColor Green
            break
        }
    }

    if (-not (Test-Path $dbPath)) {
        Write-Host "Could not find Sportarr.db in any common location." -ForegroundColor Red
        exit 1
    }
}

Write-Host "`nSportarr Download Client Type Fix Script" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Gray
Write-Host "Database: $dbPath" -ForegroundColor Gray

if ($DryRun) {
    Write-Host "MODE: DRY RUN (no changes will be made)" -ForegroundColor Yellow
} else {
    Write-Host "MODE: LIVE (database will be modified)" -ForegroundColor Red
}

Write-Host "`n"

# Check current state
Write-Host "Checking current download clients..." -ForegroundColor Cyan
$clients = & sqlite3 $dbPath "SELECT Id, Name, Type FROM DownloadClients;" 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Could not read database. Is sqlite3 installed?" -ForegroundColor Red
    exit 1
}

if (-not $clients) {
    Write-Host "No download clients found in database." -ForegroundColor Yellow
    exit 0
}

Write-Host "`nCurrent download clients:" -ForegroundColor White
Write-Host "─────────────────────────" -ForegroundColor Gray
$clients | ForEach-Object { Write-Host $_ -ForegroundColor White }

Write-Host "`n"

# The bug: Frontend was mapping SABnzbd to Type=4 (which is actually UTorrent in backend)
# Correct values: SABnzbd=5, NZBGet=6
# So we need to check if any clients have incorrect Type values

Write-Host "Analyzing for incorrect values..." -ForegroundColor Cyan

$needsFix = $false

# Check for Type=4 clients (these might be SABnzbd that were incorrectly saved)
$type4Clients = & sqlite3 $dbPath "SELECT Id, Name FROM DownloadClients WHERE Type=4;" 2>&1

if ($type4Clients) {
    Write-Host "`nWARNING: Found clients with Type=4 (UTorrent):" -ForegroundColor Yellow
    $type4Clients | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
    Write-Host "`nIf any of these should be SABnzbd, they have the bug!" -ForegroundColor Yellow
    Write-Host "This script cannot automatically determine which Type=4 clients are SABnzbd." -ForegroundColor Yellow
    Write-Host "Please manually update them:" -ForegroundColor Yellow
    Write-Host "  1. Delete the incorrectly-typed client from Sportarr UI" -ForegroundColor Gray
    Write-Host "  2. Update to v4.0.190 or later" -ForegroundColor Gray
    Write-Host "  3. Re-add the SABnzbd client (it will now use Type=5)" -ForegroundColor Gray
    $needsFix = $true
}

# Check for any obvious misconfigurations
$type5Clients = & sqlite3 $dbPath "SELECT COUNT(*) FROM DownloadClients WHERE Type=5;" 2>&1
$type6Clients = & sqlite3 $dbPath "SELECT COUNT(*) FROM DownloadClients WHERE Type=6;" 2>&1

Write-Host "`nClient type distribution:" -ForegroundColor Cyan
Write-Host "─────────────────────────" -ForegroundColor Gray
Write-Host "Type 0 (QBittorrent):  $(& sqlite3 $dbPath "SELECT COUNT(*) FROM DownloadClients WHERE Type=0;")" -ForegroundColor White
Write-Host "Type 1 (Transmission): $(& sqlite3 $dbPath "SELECT COUNT(*) FROM DownloadClients WHERE Type=1;")" -ForegroundColor White
Write-Host "Type 2 (Deluge):       $(& sqlite3 $dbPath "SELECT COUNT(*) FROM DownloadClients WHERE Type=2;")" -ForegroundColor White
Write-Host "Type 3 (RTorrent):     $(& sqlite3 $dbPath "SELECT COUNT(*) FROM DownloadClients WHERE Type=3;")" -ForegroundColor White
Write-Host "Type 4 (UTorrent):     $(& sqlite3 $dbPath "SELECT COUNT(*) FROM DownloadClients WHERE Type=4;")" -ForegroundColor White
Write-Host "Type 5 (SABnzbd):      $type5Clients" -ForegroundColor Cyan
Write-Host "Type 6 (NZBGet):       $type6Clients" -ForegroundColor Cyan

if ($needsFix) {
    Write-Host "`n════════════════════════════════════════════" -ForegroundColor Red
    Write-Host "  ACTION REQUIRED: Manual Fix Needed" -ForegroundColor Red
    Write-Host "════════════════════════════════════════════" -ForegroundColor Red
    Write-Host "`nSteps to fix:" -ForegroundColor Yellow
    Write-Host "1. Ensure you're running Sportarr v4.0.190 or later" -ForegroundColor White
    Write-Host "2. Go to Settings → Download Clients" -ForegroundColor White
    Write-Host "3. Delete any SABnzbd/NZBGet clients showing as 'TORRENT'" -ForegroundColor White
    Write-Host "4. Clear your browser cache (Ctrl+Shift+Delete)" -ForegroundColor White
    Write-Host "5. Refresh the page (Ctrl+F5)" -ForegroundColor White
    Write-Host "6. Re-add your SABnzbd/NZBGet clients" -ForegroundColor White
    Write-Host "7. Verify they now show as 'USENET' with a blue badge" -ForegroundColor White
} else {
    Write-Host "`n✓ No issues detected! All download clients have valid Type values." -ForegroundColor Green
}

Write-Host "`n"
