# Repeatable release build: tests -> self-contained publish -> portable zip -> Inno Setup installer.
# Usage: pwsh scripts/build.ps1 [-SkipTests] [-SkipInstaller]
param([switch]$SkipTests, [switch]$SkipInstaller)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$version = ([xml](Get-Content src/GardenHelper.App/GardenHelper.App.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "Garden Helper $version" -ForegroundColor Green

if (-not $SkipTests) {
    dotnet test tests/GardenHelper.Tests -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
}

$publish = Join-Path $root "publish\win-x64"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish src/GardenHelper.App -c Release -r win-x64 --self-contained true -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

New-Item -ItemType Directory -Force dist | Out-Null
$zip = Join-Path $root "dist\GardenHelper-$version-portable-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$publish\*" -DestinationPath $zip
Write-Host "Portable zip: $zip"

if (-not $SkipInstaller) {
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php or run with -SkipInstaller." }
    & $iscc "/DAppVersion=$version" "/DSourceDir=$publish" installer\GardenHelper.iss
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    Write-Host "Installer: dist\GardenHelper-Setup-$version.exe"
}
