# Test Discord Webhook for Fightarr releases
# Usage: .\test-discord-webhook.ps1 -WebhookUrl "https://discord.com/api/webhooks/..."

param(
    [Parameter(Mandatory=$true)]
    [string]$WebhookUrl
)

$VERSION = "v4.0.999.999"
$VERSION_NUMBER = "4.0.999.999"
$RELEASE_URL = "https://github.com/Fightarr/Fightarr/releases/tag/$VERSION"
$TIMESTAMP = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z")

Write-Host "Sending test notification to Discord..." -ForegroundColor Cyan
Write-Host "Version: $VERSION" -ForegroundColor Yellow
Write-Host "Release URL: $RELEASE_URL" -ForegroundColor Yellow

$body = @{
    username = "Fightarr"
    embeds = @(
        @{
            title = "New Release - $VERSION (TEST)"
            description = "**[View Release on GitHub]($RELEASE_URL)**`n`n**Docker Installation**``````docker pull fightarr/fightarr:latest`ndocker pull fightarr/fightarr:$VERSION_NUMBER``````"
            color = 5814783
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
