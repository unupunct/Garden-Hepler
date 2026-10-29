# Starts Garden Helper on the demo data (a made-up homestead near Moieciu de Sus), leaving your real data alone.
# First time, or to rebuild the demo data from scratch:  pwsh scripts/run-demo.ps1 -Rebuild
param([switch]$Rebuild, [switch]$Big, [string]$Exe)
$root = Split-Path $PSScriptRoot -Parent
$data = Join-Path $root "demo-data"
if ($Rebuild -or -not (Test-Path (Join-Path $data "gardenhelper.db"))) {
    dotnet run --project (Join-Path $root "tools\DemoSeed") -- $data (Join-Path $root "content") --force $(if ($Big) { "--big" })
}
if (-not $Exe) { $Exe = Join-Path $root "src\GardenHelper.App\bin\Debug\net10.0-windows10.0.19041.0\GardenHelper.exe" }
$env:GARDENHELPER_DATA = $data
Start-Process $Exe
Write-Host "Garden Helper started on the demo data in $data"
