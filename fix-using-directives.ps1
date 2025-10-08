# Fix using directive ordering in all C# files
# This script sorts using directives alphabetically per StyleCop SA1210

$ErrorActionPreference = "Stop"

function Sort-UsingDirectives {
    param (
        [string]$FilePath
    )

    $content = Get-Content -Path $FilePath -Raw

    # Find all using directives at the start of the file
    $pattern = '(?ms)^((?:using\s+[^;]+;\s*\n)+)'

    if ($content -match $pattern) {
        $usingBlock = $matches[1]

        # Split into individual using statements
        $usings = $usingBlock -split '\n' | Where-Object { $_ -match 'using\s+' }

        # Separate System and non-System usings
        $systemUsings = $usings | Where-Object { $_ -match 'using\s+System' } | Sort-Object
        $otherUsings = $usings | Where-Object { $_ -notmatch 'using\s+System' } | Sort-Object

        # Combine: System usings first, then others, separated by blank line
        $sortedUsings = @()
        if ($systemUsings.Count -gt 0) {
            $sortedUsings += $systemUsings
        }
        if ($otherUsings.Count -gt 0) {
            if ($systemUsings.Count -gt 0) {
                $sortedUsings += ""  # Blank line between groups
            }
            $sortedUsings += $otherUsings
        }

        $newUsingBlock = ($sortedUsings -join "`n") + "`n"

        # Replace old using block with sorted one
        $newContent = $content -replace [regex]::Escape($usingBlock), $newUsingBlock

        if ($newContent -ne $content) {
            Set-Content -Path $FilePath -Value $newContent -NoNewline
            Write-Host "Fixed: $FilePath"
            return $true
        }
    }

    return $false
}

# Get all problematic files from error messages
$files = @(
    "src\Fightarr.Http\Middleware\VersionMiddleware.cs",
    "src\Fightarr.Http\Middleware\StartingUpMiddleware.cs",
    "src\Fightarr.Http\Middleware\LoggingMiddleware.cs",
    "src\Fightarr.Http\Middleware\IfModifiedMiddleware.cs",
    "src\Fightarr.Http\Middleware\CacheHeaderMiddleware.cs",
    "src\Fightarr.Http\Frontend\StaticResourceController.cs",
    "src\Fightarr.Http\ErrorManagement\FightarrErrorPipeline.cs",
    "src\Fightarr.Http\ErrorManagement\ErrorModel.cs",
    "src\Fightarr.Http\Authentication\UiAuthorizationHandler.cs",
    "src\Fightarr.Http\Authentication\AuthenticationService.cs"
)

$fixedCount = 0
foreach ($file in $files) {
    $fullPath = Join-Path $PSScriptRoot $file
    if (Test-Path $fullPath) {
        if (Sort-UsingDirectives -FilePath $fullPath) {
            $fixedCount++
        }
    } else {
        Write-Warning "File not found: $fullPath"
    }
}

Write-Host "`nFixed $fixedCount files"
