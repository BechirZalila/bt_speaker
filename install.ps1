<#
.SYNOPSIS
    Compile et installe BT Battery Tray pour l'utilisateur courant (sans droits administrateur).

.DESCRIPTION
    - Publie l'application en un seul exe (nécessite le SDK .NET 8).
    - La copie dans %LOCALAPPDATA%\BtBatteryTray.
    - Active le lancement au démarrage de Windows (sauf avec -NoStartup).
    - Lance l'application.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -NoStartup
#>
param(
    [switch]$NoStartup
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\BtBatteryTray\BtBatteryTray.csproj'
$installDir = Join-Path $env:LOCALAPPDATA 'BtBatteryTray'
$exe = Join-Path $installDir 'BtBatteryTray.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Write-Host 'Compilation...'
$publishDir = Join-Path ([System.IO.Path]::GetTempPath()) "BtBatteryTray-publish-$PID"
dotnet publish $project -c Release -o $publishDir -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'La compilation a échoué.' }

Write-Host 'Arrêt de la version en cours d''exécution...'
Get-Process BtBatteryTray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "Installation dans $installDir..."
New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item (Join-Path $publishDir 'BtBatteryTray.exe') $exe -Force
Remove-Item $publishDir -Recurse -Force

if ($NoStartup) {
    Remove-ItemProperty $runKey -Name 'BtBatteryTray' -ErrorAction SilentlyContinue
}
else {
    Write-Host 'Activation du lancement au démarrage de Windows...'
    Set-ItemProperty $runKey -Name 'BtBatteryTray' -Value "`"$exe`""
}

Start-Process $exe
Write-Host 'BT Battery Tray est installé et lancé.' -ForegroundColor Green
Write-Host 'Astuce : si les icônes sont cachées, clique sur ^ près de l''horloge et fais-les glisser dans la barre des tâches.'
