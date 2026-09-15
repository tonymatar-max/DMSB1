<#
.SYNOPSIS
    Installs Nexus Docs as a Windows service. Run from an elevated PowerShell.

.DESCRIPTION
    Creates appsettings.Production.json on first run with a generated JWT signing key, registers
    the service, sets it to restart on failure, and starts it.

    Re-running is safe: the service is stopped, updated and restarted, and an existing production
    settings file is left alone (so a signing key or ERP connection secret set after install is
    never clobbered by a later re-run).

.EXAMPLE
    .\install-service.ps1
    .\install-service.ps1 -InstallPath "C:\NexusDocs" -Port 5210
    .\install-service.ps1 -ServiceAccount "DOMAIN\svc_nexusdocs"
#>
[CmdletBinding()]
param(
    [string]$InstallPath = "C:\NexusDocs",
    [string]$ServiceName = "NexusDocs",
    [string]$DisplayName = "Nexus Docs",
    [int]$Port           = 5210,
    [string]$ServiceAccount
)

$ErrorActionPreference = "Stop"

# --- must be elevated: creating a service and writing to Program Files both need it
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run from an elevated PowerShell (Run as administrator)."
}

$exePath = Join-Path $InstallPath "NexusDocs.Api.exe"
if (-not (Test-Path $exePath)) {
    throw "$exePath was not found. Run publish.ps1 first."
}

Write-Host "Installing $DisplayName" -ForegroundColor Cyan
Write-Host "  path: $InstallPath"
Write-Host "  port: $Port"

# --- the data folder holds the database, blob store and Data Protection keys, and must survive
#     redeploys (publish.ps1 already preserves the database file itself, but not these others)
$dataPath = Join-Path $InstallPath "data"
$blobsPath = Join-Path $dataPath "blobs"
$keysPath = Join-Path $dataPath "keys"
foreach ($p in @($dataPath, $blobsPath, $keysPath)) {
    if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
}

# --- production settings, created once and never overwritten
$configPath = Join-Path $InstallPath "appsettings.Production.json"
if (-not (Test-Path $configPath)) {
    # A per-installation signing key. Tokens signed by one deployment must not validate on another.
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $signingKey = [Convert]::ToBase64String($bytes)

    $settings = [ordered]@{
        Logging = [ordered]@{
            LogLevel = [ordered]@{
                Default                = "Information"
                "Microsoft.AspNetCore" = "Warning"
            }
        }
        AllowedHosts      = "*"
        Urls              = "http://localhost:$Port"
        ConnectionStrings = [ordered]@{ Default = "Data Source=data\nexusdocs.db" }
        BlobStore         = [ordered]@{ RootPath = "data\blobs" }
        DataProtection    = [ordered]@{ KeysPath = "data\keys" }
        Jwt               = [ordered]@{ SigningKey = $signingKey }
    }

    $settings | ConvertTo-Json -Depth 6 | Set-Content -Path $configPath -Encoding utf8
    Write-Host "  created appsettings.Production.json with a generated signing key" -ForegroundColor Green
    Write-Host "  no tenant/user exists yet in Production mode by design (the seeded dev login" -ForegroundColor Yellow
    Write-Host "  only runs in Development) - see the app's own startup log for how to proceed." -ForegroundColor Yellow
}
else {
    Write-Host "  appsettings.Production.json already exists, leaving it untouched"
}

# --- stop and remove any previous registration so this script is safe to re-run
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "  stopping the existing service"
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        $existing.WaitForStatus("Stopped", "00:00:30")
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# --- register. binPath is quoted because the install path may contain spaces.
$binPath = '"{0}"' -f $exePath
$arguments = @{
    Name           = $ServiceName
    BinaryPathName = $binPath
    DisplayName    = $DisplayName
    Description    = "Nexus Docs - document archive, approval workflow and e-signature."
    StartupType    = "Automatic"
}
if ($ServiceAccount) {
    $credential = Get-Credential -UserName $ServiceAccount -Message "Password for $ServiceAccount"
    $arguments.Credential = $credential
}

New-Service @arguments | Out-Null
Write-Host "  service registered" -ForegroundColor Green

# --- environment for the service process
[Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production", "Machine")

# --- restart automatically after a crash rather than sitting dead until someone notices
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

# --- a service account needs write access to data\ for the database, blobs and DP keys
if ($ServiceAccount) {
    $acl = Get-Acl $dataPath
    $rule = New-Object Security.AccessControl.FileSystemAccessRule(
        $ServiceAccount, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.AddAccessRule($rule)
    Set-Acl -Path $dataPath -AclObject $acl
    Write-Host "  granted $ServiceAccount modify rights on data\"
}

Write-Host "`n  starting..." -ForegroundColor Cyan
Start-Service -Name $ServiceName
Start-Sleep -Seconds 5

$service = Get-Service -Name $ServiceName
Write-Host "  status: $($service.Status)" -ForegroundColor $(if ($service.Status -eq "Running") { "Green" } else { "Red" })

try {
    $health = Invoke-RestMethod -Uri "http://localhost:$Port/health" -TimeoutSec 10
    Write-Host "  health: $($health.status) ($($health.environment))" -ForegroundColor Green
    Write-Host "`nNexus Docs is available at http://localhost:$Port" -ForegroundColor Green
}
catch {
    Write-Host "  health check failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  check the Application event log, source '$ServiceName'" -ForegroundColor Yellow
    Write-Host "  Get-EventLog -LogName Application -Source '$ServiceName' -Newest 20 | Format-List"
}
