<#
.SYNOPSIS
    Compile l'application puis l'assistant d'installation : artifacts\BtBatteryTray-Setup-<version>.exe

.DESCRIPTION
    Nécessite le SDK .NET 8 et Inno Setup 6 (winget install --id JRSoftware.InnoSetup -e).
    La version est lue dans src\BtBatteryTray\BtBatteryTray.csproj.
#>
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\BtBatteryTray\BtBatteryTray.csproj'
$publishDir = Join-Path $root 'artifacts\publish'
$script = Join-Path $root 'installer\BtBatteryTray.iss'

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Version introuvable dans $project" }

$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 introuvable. Installer : winget install --id JRSoftware.InnoSetup -e' }

Write-Host "Compilation de l'application $version..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $project -c Release -o $publishDir -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'La compilation de l''application a échoué.' }

Write-Host "Compilation de l'assistant d'installation..."
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$publishDir" $script
if ($LASTEXITCODE -ne 0) { throw 'La compilation de l''assistant a échoué.' }

$setup = Join-Path $root "artifacts\BtBatteryTray-Setup-$version.exe"
Write-Host "Assistant créé : $setup" -ForegroundColor Green
