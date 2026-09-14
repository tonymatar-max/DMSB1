<#
.SYNOPSIS
    Builds Nexus Docs into a single self-contained folder ready to run as a Windows service.

.DESCRIPTION
    Builds the React client, copies it into the API's wwwroot, and publishes the API. The result is
    one folder that serves both the API and the app, so there is no second web server to install.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -OutputPath "C:\NexusDocs" -SkipClient
#>
[CmdletBinding()]
param(
    [string]$OutputPath = "C:\NexusDocs",
    [switch]$SkipClient
)

$ErrorActionPreference = "Stop"

$root      = Split-Path -Parent $PSScriptRoot
$serverDir = Join-Path $root "server"
$clientDir = Join-Path $root "client"
$wwwroot   = Join-Path $serverDir "wwwroot"

Write-Host "Publishing Nexus Docs" -ForegroundColor Cyan
Write-Host "  source: $root"
Write-Host "  target: $OutputPath"

if (-not $SkipClient) {
    Write-Host "`n[1/3] Building the client..." -ForegroundColor Cyan
    Push-Location $clientDir
    try {
        if (-not (Test-Path (Join-Path $clientDir "node_modules"))) {
            Write-Host "  installing dependencies (first run)"
            npm install --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
        }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "The client build failed" }
    }
    finally { Pop-Location }

    # wwwroot is rebuilt each time so a file deleted from the client cannot linger in the deployment.
    if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
    New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
    Copy-Item (Join-Path $clientDir "dist\*") $wwwroot -Recurse -Force
    Write-Host "  client copied into wwwroot"
}
else {
    Write-Host "`n[1/3] Skipping the client build" -ForegroundColor Yellow
}

Write-Host "`n[2/3] Publishing the API..." -ForegroundColor Cyan
# The published folder is replaced, but the dev database and any Production settings are preserved.
$dbBackup = $null
$configBackup = $null
$dbPath = Join-Path $OutputPath "nexusdocs.dev.db"
if (Test-Path $dbPath) {
    $dbBackup = Join-Path $env:TEMP ("nexusdocs-db-" + [guid]::NewGuid().ToString("N") + ".db")
    Copy-Item $dbPath $dbBackup -Force
    Write-Host "  existing database set aside"
}
if (Test-Path (Join-Path $OutputPath "appsettings.Production.json")) {
    $configBackup = Join-Path $env:TEMP ("nexusdocs-cfg-" + [guid]::NewGuid().ToString("N") + ".json")
    Copy-Item (Join-Path $OutputPath "appsettings.Production.json") $configBackup -Force
    Write-Host "  existing production settings set aside"
}

dotnet publish (Join-Path $serverDir "NexusDocs.Api.csproj") `
    --configuration Release `
    --output $OutputPath `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if ($dbBackup) {
    Copy-Item $dbBackup $dbPath -Force
    Remove-Item $dbBackup -Force
    Write-Host "  database restored"
}
if ($configBackup) {
    Copy-Item $configBackup (Join-Path $OutputPath "appsettings.Production.json") -Force
    Remove-Item $configBackup -Force
    Write-Host "  production settings restored"
}

Write-Host "`n[3/3] Done." -ForegroundColor Green
Write-Host "  Published to: $OutputPath"
Write-Host "  Set Jwt:SigningKey in appsettings.Production.json before running outside dev."
