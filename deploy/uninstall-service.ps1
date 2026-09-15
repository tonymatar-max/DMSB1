<#
.SYNOPSIS
    Removes the Nexus Docs Windows service. Run from an elevated PowerShell.

.DESCRIPTION
    Stops and deletes the service. The installation folder, database, blob store and Data
    Protection keys are left in place unless -RemoveFiles is passed, because deleting a customer's
    archived documents by accident is not a recoverable mistake.

.EXAMPLE
    .\uninstall-service.ps1
    .\uninstall-service.ps1 -RemoveFiles
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
param(
    [string]$ServiceName = "NexusDocs",
    [string]$InstallPath = "C:\NexusDocs",
    [switch]$RemoveFiles
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run from an elevated PowerShell (Run as administrator)."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$ServiceName' is not installed." -ForegroundColor Yellow
}
else {
    if ($service.Status -ne "Stopped") {
        Write-Host "Stopping $ServiceName..."
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus("Stopped", "00:00:30")
    }
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service removed." -ForegroundColor Green
}

if ($RemoveFiles) {
    if ($PSCmdlet.ShouldProcess($InstallPath, "Delete the installation folder, database, blob store and Data Protection keys")) {
        Remove-Item $InstallPath -Recurse -Force
        Write-Host "Deleted $InstallPath" -ForegroundColor Yellow
    }
}
else {
    Write-Host "Files left in place at $InstallPath (pass -RemoveFiles to delete them)."
}
