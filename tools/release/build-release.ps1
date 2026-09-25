# Builds a shareable HAULIX release:
#   dist\Haulix.exe                      the installer (app embedded, no .NET download needed)
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
# are unpacked to %TEMP% on first start). Only the UI (wwwroot) and licences stay as files.
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
Copy-Item NOTICE.md (Join-Path $appOut "NOTICE.txt")
Copy-Item THIRD-PARTY-NOTICES.md (Join-Path $appOut "THIRD-PARTY-NOTICES.txt")
Set-Content -Path (Join-Path $appOut "SOURCE.txt") -Encoding UTF8 -Value @"
HAULIX ETS2 Logger $Version
Copyright © 2026 RyanTMP. HAULIX is licensed under the GNU General Public License v2 (see LICENSE.txt).
The HAULIX name, logo and artwork are © RyanTMP, all rights reserved (see NOTICE.txt).
The complete source code is distributed alongside this program as HAULIX-$Version-source.zip.
"@

$payload = Join-Path $artifacts "payload.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($appOut, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "3/4 Building the installer"
$setupOut = Join-Path $artifacts "setup"
dotnet build src\Haulix.Installer -c Release -o $setupOut -p:Payload="$payload" -p:Version=$Version --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Invoke-HaulixSign -Files @(Join-Path $setupOut "HAULIX-Setup.exe") -Required:$RequireSigning | Out-Null
# The download is always called Haulix.exe (it installs or updates HAULIX); older versioned setups are removed.
Get-ChildItem $dist -Filter "HAULIX-Setup-*.exe" -ErrorAction SilentlyContinue | ForEach-Object { [IO.File]::Delete($_.FullName) }
$setup = Join-Path $dist "Haulix.exe"
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
