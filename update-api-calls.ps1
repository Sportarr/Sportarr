# PowerShell script to update all settings files to use authenticated API helpers

$files = @(
    "frontend/src/pages/settings/MediaManagementSettings.tsx",
    "frontend/src/pages/settings/NotificationsSettings.tsx",
    "frontend/src/pages/settings/GeneralSettings.tsx",
    "frontend/src/pages/settings/UISettings.tsx",
    "frontend/src/pages/settings/ProfilesSettings.tsx",
    "frontend/src/pages/settings/CustomFormatsSettings.tsx"
)

foreach ($file in $files) {
    Write-Host "Processing $file..." -ForegroundColor Green

    $content = Get-Content $file -Raw

    # Add import statement if not already present
    if ($content -notmatch "import.*api.*from.*utils/api") {
        $content = $content -replace "(import.*from '@heroicons/react/24/outline';)", "`$1`nimport { apiGet, apiPost, apiPut, apiDelete } from '../../utils/api';"
    }

    # Replace fetch GET calls
    $content = $content -replace "await fetch\('/api/([^']+)'\)", "await apiGet('/api/`$1')"

    # Replace fetch POST calls
    $content = $content -replace "await fetch\('/api/([^']+)',\s*\{\s*method:\s*'POST',\s*headers:\s*\{\s*'Content-Type':\s*'application/json'\s*\},\s*body:\s*JSON\.stringify\(([^)]+)\)\s*\}\)", "await apiPost('/api/`$1', `$2)"

    # Replace fetch PUT calls
    $content = $content -replace "await fetch\(`"\/api\/([^`"]+)`",\s*\{\s*method:\s*'PUT',\s*headers:\s*\{\s*'Content-Type':\s*'application/json'\s*\},\s*body:\s*JSON\.stringify\(([^)]+)\)\s*\}\)", "await apiPut(`"/api/`$1`", `$2)"

    # Replace fetch DELETE calls
    $content = $content -replace "await fetch\(`"\/api\/([^`"]+)`",\s*\{\s*method:\s*'DELETE'\s*\}\)", "await apiDelete(`"/api/`$1`")"

    Set-Content -Path $file -Value $content -NoNewline
    Write-Host "  Updated $file" -ForegroundColor Cyan
}

Write-Host "`nAll files updated successfully!" -ForegroundColor Green
