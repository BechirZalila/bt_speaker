<#
.SYNOPSIS
    Désinstalle BT Battery Tray de l'utilisateur courant.

.EXAMPLE
    .\uninstall.ps1                 # conserve les préférences
    .\uninstall.ps1 -RemoveSettings # supprime aussi les préférences
#>
param(
    [switch]$RemoveSettings
)

$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:LOCALAPPDATA 'BtBatteryTray'
$settingsDir = Join-Path $env:APPDATA 'BtBatteryTray'

Get-Process BtBatteryTray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'BtBatteryTray' -ErrorAction SilentlyContinue

if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
if ($RemoveSettings -and (Test-Path $settingsDir)) { Remove-Item $settingsDir -Recurse -Force }

Write-Host 'BT Battery Tray est désinstallé.' -ForegroundColor Green
