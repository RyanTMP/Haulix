# Publishes a HAULIX release on GitHub. Installed copies of HAULIX find it automatically (Settings → About →
# Check for updates) – GitHub hosts everything, no own server needed.
#
#   powershell -ExecutionPolicy Bypass -File tools\release\publish-github.ps1
#
# Steps: build the setup (build-release.ps1), update CHANGELOG.md, commit + push, tag v<version>, create the
# GitHub release with the setup and the source zip, notes taken from changelog.json.
# Requires: git, GitHub CLI (gh) logged in (gh auth login), and the version in the csproj files.
param([switch]$SkipBuild, [switch]$Draft)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
Set-Location $root

[xml]$proj = Get-Content "src\Haulix.App\Haulix.App.csproj"
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$tag = "v$version"
$label = [regex]::Replace($version, '-([a-z]+)$', { param($m) " " + $m.Groups[1].Value.ToUpper() })
Write-Host "Publishing HAULIX $label ($tag)" -ForegroundColor Yellow

gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "GitHub CLI is not logged in. Run: gh auth login" }
if (git tag --list $tag) { throw "Tag $tag already exists. Raise the version in the csproj files first." }

if (-not $SkipBuild) {
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\build-release.ps1
  if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}
$setup = "dist\Haulix.exe"
$source = "dist\HAULIX-$version-source.zip"
foreach ($f in $setup, $source) { if (-not (Test-Path $f)) { throw "$f is missing" } }

$notes = Join-Path $env:TEMP "haulix-release-notes.md"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\changelog.ps1 -Version $version -NotesFile $notes

git add -A
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) { git commit -m "Release $label" }
git tag -a $tag -m "HAULIX $label"
git push origin HEAD --follow-tags

# Only Haulix.exe is attached; GitHub adds the source code archives of the tag automatically (GPL source).
$ghArgs = @("release", "create", $tag, $setup, "--title", "HAULIX $label", "--notes-file", $notes)
if ($Draft) { $ghArgs += "--draft" }
gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
Write-Host "Done: release $tag is live. Installed HAULIX copies will offer the update." -ForegroundColor Green
