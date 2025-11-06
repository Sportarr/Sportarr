# Comprehensive diagnostic for SABnzbd showing as TORRENT instead of USENET
# This will check EVERY step of the data flow to find the real issue

param(
    [string]$FightarrUrl = "http://localhost:5000"
)

Write-Host "`n╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Fightarr Download Client Type Diagnostic                    ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

# Test 1: Check Database
Write-Host "`n[TEST 1] Checking database values..." -ForegroundColor Yellow
Write-Host "────────────────────────────────────────────────────────────────" -ForegroundColor Gray

$dbPath = "$PSScriptRoot\..\src\bin\Debug\net9.0\Fightarr.db"
$altPaths = @(
    "$PSScriptRoot\..\src\bin\Release\net9.0\Fightarr.db",
    "$env:APPDATA\Fightarr\Fightarr.db",
    "$env:ProgramData\Fightarr\Fightarr.db"
)

foreach ($path in @($dbPath) + $altPaths) {
    if (Test-Path $path) {
        $dbPath = $path
        Write-Host "✓ Found database: $dbPath" -ForegroundColor Green
        break
    }
}

if (Test-Path $dbPath) {
    try {
        $result = sqlite3 $dbPath "SELECT Id, Name, Type, Host, Port, Enabled FROM DownloadClients;" 2>&1

        if ($LASTEXITCODE -eq 0) {
            Write-Host "`nDatabase Contents:" -ForegroundColor White
            if ($result) {
                $result | ForEach-Object {
                    $parts = $_ -split '\|'
                    if ($parts.Length -ge 3) {
                        $type = [int]$parts[2]
                        $typeName = switch ($type) {
                            0 { "QBittorrent (Torrent)" }
                            1 { "Transmission (Torrent)" }
                            2 { "Deluge (Torrent)" }
                            3 { "RTorrent (Torrent)" }
                            4 { "UTorrent (Torrent)" }
                            5 { "SABnzbd (Usenet)" }
                            6 { "NZBGet (Usenet)" }
                            default { "UNKNOWN" }
                        }

                        $color = if ($type -eq 5 -or $type -eq 6) { "Cyan" } else { "Green" }
                        Write-Host "  ID=$($parts[0]) | Name=$($parts[1]) | Type=$type ($typeName)" -ForegroundColor $color

                        # Flag suspicious values
                        if ($parts[1] -like "*SAB*" -and $type -ne 5) {
                            Write-Host "  ⚠ WARNING: This looks like SABnzbd but has Type=$type (should be 5)" -ForegroundColor Red
                        }
                        if ($parts[1] -like "*NZB*" -and $type -ne 6) {
                            Write-Host "  ⚠ WARNING: This looks like NZBGet but has Type=$type (should be 6)" -ForegroundColor Red
                        }
                    }
                }
            } else {
                Write-Host "  No download clients in database" -ForegroundColor Yellow
            }
        } else {
            Write-Host "✗ Could not query database (sqlite3 not available)" -ForegroundColor Red
        }
    } catch {
        Write-Host "✗ Error querying database: $_" -ForegroundColor Red
    }
} else {
    Write-Host "✗ Database not found" -ForegroundColor Red
}

# Test 2: Check API Response
Write-Host "`n[TEST 2] Checking API endpoint response..." -ForegroundColor Yellow
Write-Host "────────────────────────────────────────────────────────────────" -ForegroundColor Gray

try {
    $apiResponse = Invoke-RestMethod -Uri "$FightarrUrl/api/downloadclient" -Method Get -ErrorAction Stop

    Write-Host "✓ API Response received" -ForegroundColor Green
    Write-Host "`nAPI Returns:" -ForegroundColor White

    $apiResponse | ForEach-Object {
        $typeName = switch ($_.type) {
            0 { "QBittorrent (Torrent)" }
            1 { "Transmission (Torrent)" }
            2 { "Deluge (Torrent)" }
            3 { "RTorrent (Torrent)" }
            4 { "UTorrent (Torrent)" }
            5 { "SABnzbd (Usenet)" }
            6 { "NZBGet (Usenet)" }
            default { "UNKNOWN" }
        }

        $color = if ($_.type -eq 5 -or $_.type -eq 6) { "Cyan" } else { "Green" }
        Write-Host "  ID=$($_.id) | Name=$($_.name) | Type=$($_.type) ($typeName)" -ForegroundColor $color

        if ($_.name -like "*SAB*" -and $_.type -ne 5) {
            Write-Host "  ⚠ WARNING: API returning wrong type for SABnzbd!" -ForegroundColor Red
        }
    }
} catch {
    Write-Host "✗ Could not connect to API at $FightarrUrl" -ForegroundColor Red
    Write-Host "  Error: $_" -ForegroundColor Red
    Write-Host "  Make sure Fightarr is running!" -ForegroundColor Yellow
}

# Test 3: Check Frontend Build
Write-Host "`n[TEST 3] Checking frontend build..." -ForegroundColor Yellow
Write-Host "────────────────────────────────────────────────────────────────" -ForegroundColor Gray

$wwwrootJs = "$PSScriptRoot\..\src\wwwroot\assets\index-DarmdC30.js"
if (Test-Path $wwwrootJs) {
    Write-Host "✓ Frontend JS found: $wwwrootJs" -ForegroundColor Green

    # Check for the protocol detection function
    $jsContent = Get-Content $wwwrootJs -Raw

    # Search for type 5 and 6 checks (these would be in the minified code)
    if ($jsContent -match '===5.*===6.*usenet|===6.*===5.*usenet') {
        Write-Host "✓ Frontend code contains correct usenet detection (type 5 or 6)" -ForegroundColor Green
    } elseif ($jsContent -match '===4.*===5.*usenet|===5.*===4.*usenet') {
        Write-Host "✗ FOUND THE BUG! Frontend code checking types 4 & 5 for usenet (OLD BUG)" -ForegroundColor Red
        Write-Host "  This means the frontend build is OUTDATED!" -ForegroundColor Red
    } else {
        Write-Host "⚠ Could not find protocol detection code in minified JS" -ForegroundColor Yellow
        Write-Host "  (This is expected with heavy minification)" -ForegroundColor Gray
    }

    # Check file modification date
    $fileDate = (Get-Item $wwwrootJs).LastWriteTime
    Write-Host "`nFrontend build date: $fileDate" -ForegroundColor White

    # Check if it matches the source
    $sourceFile = "$PSScriptRoot\..\frontend\src\pages\settings\DownloadClientsSettings.tsx"
    if (Test-Path $sourceFile) {
        $sourceDate = (Get-Item $sourceFile).LastWriteTime
        Write-Host "Source file date:    $sourceDate" -ForegroundColor White

        if ($fileDate -lt $sourceDate) {
            Write-Host "✗ PROBLEM: Frontend build is OLDER than source!" -ForegroundColor Red
            Write-Host "  You need to rebuild: cd frontend && npm run build" -ForegroundColor Yellow
        } else {
            Write-Host "✓ Frontend build is up to date" -ForegroundColor Green
        }
    }
} else {
    Write-Host "✗ Frontend JS not found at $wwwrootJs" -ForegroundColor Red
}

# Test 4: Check Source Code
Write-Host "`n[TEST 4] Checking source code..." -ForegroundColor Yellow
Write-Host "────────────────────────────────────────────────────────────────" -ForegroundColor Gray

$sourceFile = "$PSScriptRoot\..\frontend\src\pages\settings\DownloadClientsSettings.tsx"
if (Test-Path $sourceFile) {
    $sourceContent = Get-Content $sourceFile -Raw

    if ($sourceContent -match 'type === 5 \|\| type === 6.*usenet') {
        Write-Host "✓ Source code has CORRECT protocol detection (5 || 6)" -ForegroundColor Green
    } elseif ($sourceContent -match 'type === 4 \|\| type === 5.*usenet') {
        Write-Host "✗ Source code has OLD BUG (4 || 5)" -ForegroundColor Red
    } else {
        Write-Host "⚠ Could not find protocol detection in source" -ForegroundColor Yellow
    }

    # Check client type map
    if ($sourceContent -match "'SABnzbd':\s*5") {
        Write-Host "✓ Source code maps SABnzbd to type 5" -ForegroundColor Green
    } elseif ($sourceContent -match "'SABnzbd':\s*4") {
        Write-Host "✗ Source code maps SABnzbd to type 4 (WRONG!)" -ForegroundColor Red
    }
} else {
    Write-Host "✗ Source file not found" -ForegroundColor Red
}

# Test 5: Check Backend Enum
Write-Host "`n[TEST 5] Checking backend enum definition..." -ForegroundColor Yellow
Write-Host "────────────────────────────────────────────────────────────────" -ForegroundColor Gray

$backendFile = "$PSScriptRoot\..\src\Models\Download.cs"
if (Test-Path $backendFile) {
    $backendContent = Get-Content $backendFile -Raw

    if ($backendContent -match 'Sabnzbd\s*=\s*5') {
        Write-Host "✓ Backend enum: Sabnzbd = 5" -ForegroundColor Green
    } else {
        Write-Host "✗ Backend enum incorrect!" -ForegroundColor Red
    }

    if ($backendContent -match 'NzbGet\s*=\s*6') {
        Write-Host "✓ Backend enum: NzbGet = 6" -ForegroundColor Green
    } else {
        Write-Host "✗ Backend enum incorrect!" -ForegroundColor Red
    }

    if ($backendContent -match 'UTorrent\s*=\s*4') {
        Write-Host "✓ Backend enum: UTorrent = 4" -ForegroundColor Green
    } else {
        Write-Host "⚠ Backend missing UTorrent = 4" -ForegroundColor Yellow
    }
} else {
    Write-Host "✗ Backend file not found" -ForegroundColor Red
}

# Summary
Write-Host "`n╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  DIAGNOSTIC SUMMARY                                           ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

Write-Host "`nCheck the results above for any RED warnings." -ForegroundColor White
Write-Host "`nCommon issues:" -ForegroundColor Yellow
Write-Host "  1. Database has Type=4 for SABnzbd → Delete and re-add client" -ForegroundColor Gray
Write-Host "  2. Frontend build is outdated → Rebuild frontend" -ForegroundColor Gray
Write-Host "  3. wwwroot has old files → Copy new build to wwwroot" -ForegroundColor Gray
Write-Host "  4. Browser cache → Hard refresh (Ctrl+F5)" -ForegroundColor Gray

Write-Host "`n"
