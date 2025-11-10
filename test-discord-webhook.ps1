# Test Discord Webhook for Sportarr releases
# Usage: .\test-discord-webhook.ps1 -WebhookUrl "https://discord.com/api/webhooks/..."

param(
    [Parameter(Mandatory=$true)]
    [string]$WebhookUrl
)

$VERSION = "v4.0.999.999"
$VERSION_NUMBER = "4.0.999.999"
$RELEASE_URL = "https://github.com/Sportarr/Sportarr/releases/tag/$VERSION"
$CHANGELOG_URL = "https://github.com/Sportarr/Sportarr/blob/main/CHANGELOG.md"
$TIMESTAMP = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z")

Write-Host "Sending test notification to Discord..." -ForegroundColor Cyan
Write-Host "Version: $VERSION" -ForegroundColor Yellow
Write-Host "Release URL: $RELEASE_URL" -ForegroundColor Yellow

# Get recent commits for the test (last 5)
$commits = git log --pretty=format:"• %s" -n 5 --no-merges | Out-String
$commits = $commits.Trim()

$description = "**What's New**`n$commits`n`n**[📋 View Full Changelog]($CHANGELOG_URL)** • **[📦 View Release]($RELEASE_URL)**`n`n**Docker Installation**``````docker pull sportarr/sportarr:latest`ndocker pull sportarr/sportarr:$VERSION_NUMBER``````"

$body = @{
    username = "Sportarr"
    embeds = @(
        @{
            title = "New Release - $VERSION (TEST)"
            description = $description
            color = 14427686
            timestamp = $TIMESTAMP
        }
    )
} | ConvertTo-Json -Depth 4

try {
    $response = Invoke-RestMethod -Uri $WebhookUrl -Method Post -Body $body -ContentType 'application/json'
    Write-Host "`nTest notification sent successfully! Check your Discord server." -ForegroundColor Green
} catch {
    Write-Host "`nError sending notification:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
