# Builds a shareable HAULIX release:
#   dist\HAULIX-Setup-<version>.exe      single-file installer (app embedded, no .NET download needed)
#   dist\HAULIX-<version>-source.zip     complete source code (required by the GPL when you share the exe)
#
# Usage (from the repo root):
#   powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1
#   powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1 -Version 0.0.5-beta
#   add -RequireSigning to fail instead of building unsigned when no certificate is available
param([string]$Version = "", [switch]$RequireSigning)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
Set-Location $root
Add-Type -AssemblyName System.IO.Compression.FileSystem
. "$PSScriptRoot\sign.ps1"   # code signing (Certum via SimplySign); skipped with a warning when no certificate is present

if (-not $Version) {
  [xml]$proj = Get-Content "src\Haulix.App\Haulix.App.csproj"
  $Version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}
Write-Host "Building HAULIX $Version" -ForegroundColor Yellow

$artifacts = Join-Path $root "artifacts"
$dist = Join-Path $root "dist"
Remove-Item -Recurse -Force $artifacts -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $artifacts, $dist | Out-Null

Write-Host "1/4 Running tests"
dotnet test tests\Haulix.Core.Tests -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

Write-Host "2/4 Publishing the app (self-contained, win-x64)"
$appOut = Join-Path $artifacts "app"
# One program file: the .NET runtime, all libraries and native DLLs are bundled into Haulix.exe (native ones
# are unpacked to %TEMP% on first start). Only the UI (wwwroot), the map bundle and licences stay as files.
dotnet publish src\Haulix.App -c Release -r win-x64 --self-contained true -o $appOut `
  -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
# createdump.exe is the runtime's crash-dump helper; HAULIX does not need it.
Get-ChildItem $appOut -Filter *.exe | Where-Object { $_.Name -ne "Haulix.exe" } | ForEach-Object { [IO.File]::Delete($_.FullName) }
# IntelliSense docs of the WebView2 package are not needed at runtime.
Get-ChildItem $appOut -Filter *.xml | ForEach-Object { [IO.File]::Delete($_.FullName) }
Invoke-HaulixSign -Files @(Join-Path $appOut "Haulix.exe") -Required:$RequireSigning | Out-Null
Copy-Item LICENSE (Join-Path $appOut "LICENSE.txt")
Copy-Item third_party\scs-sdk-plugin\LICENSE (Join-Path $appOut "LICENSE-scs-sdk-plugin.txt")
Set-Content -Path (Join-Path $appOut "SOURCE.txt") -Encoding UTF8 -Value @"
HAULIX ETS2 Logger $Version
HAULIX is free software under the GNU General Public License v2 (see LICENSE.txt).
The complete source code is distributed alongside this program as HAULIX-$Version-source.zip.
"@

# Full map: ship the road map built on this PC so players with fewer map DLCs (or without the game
# installed) still see every street. Build it first by starting HAULIX once (Settings -> Map).
$mapDir = Join-Path $env:LOCALAPPDATA "Haulix\map"
$meta = Get-ChildItem $mapDir -Filter "meta-*.json" -ErrorAction SilentlyContinue |
  Where-Object { Test-Path (Join-Path $mapDir ("network-" + $_.BaseName.Substring(5) + ".bin")) } |
  Sort-Object { ((Get-Content $_.FullName -Raw | ConvertFrom-Json).mapDlcs).Count }, LastWriteTime | Select-Object -Last 1
if ($meta) {
  $key = $meta.BaseName.Substring(5)
  $bundle = Join-Path $appOut "map-bundle"
  New-Item -ItemType Directory -Force $bundle | Out-Null
  foreach ($f in @("network-$key.bin", "streets-$key.bin", "pois-$key.json", "vcountry-$key.bin")) {
    $src = Join-Path $mapDir $f
    if (-not (Test-Path $src)) { throw "Map bundle incomplete: $f missing (rebuild the road map in HAULIX)" }
    Copy-Item $src $bundle
  }
  Get-ChildItem $mapDir -Filter "countries-$key-v*.json" | Copy-Item -Destination $bundle
  Get-ChildItem $mapDir -Filter "land-$key-v*.png" | Copy-Item -Destination $bundle
  Copy-Item $meta.FullName (Join-Path $bundle "bundle.json")
  $dlcs = (Get-Content $meta.FullName -Raw | ConvertFrom-Json).mapDlcs
  Write-Host ("    full map bundled: {0} map DLCs ({1})" -f $dlcs.Count, ($dlcs -join ", "))
} else {
  Write-Warning "No built road map found in $mapDir - the release will not include the full map."
}

$payload = Join-Path $artifacts "payload.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($appOut, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "3/4 Building the installer"
$setupOut = Join-Path $artifacts "setup"
dotnet build src\Haulix.Installer -c Release -o $setupOut -p:Payload="$payload" -p:Version=$Version --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Invoke-HaulixSign -Files @(Join-Path $setupOut "HAULIX-Setup.exe") -Required:$RequireSigning | Out-Null
$setup = Join-Path $dist "HAULIX-Setup-$Version.exe"
Copy-Item (Join-Path $setupOut "HAULIX-Setup.exe") $setup -Force

Write-Host "4/4 Packaging source code"
$source = Join-Path $dist "HAULIX-$Version-source.zip"
if (Test-Path $source) { Remove-Item $source }
$zip = [IO.Compression.ZipFile]::Open($source, "Create")
try {
  $skip = '\\(bin|obj|artifacts|dist|\.vs|TestResults|node_modules)\\|\\wwwroot\\data\\'
  Get-ChildItem $root -Recurse -File | Where-Object { ($_.FullName + "\") -notmatch $skip -and $_.FullName -notmatch '\\\.git\\' } | ForEach-Object {
    $rel = $_.FullName.Substring($root.Length + 1)
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $rel, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
} finally { $zip.Dispose() }

$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host ("  {0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
Write-Host ("  {0}  ({1:N1} MB)" -f $source, ((Get-Item $source).Length / 1MB))
Write-Host "  SHA-256: $hash"
