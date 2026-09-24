# Generates CHANGELOG.md (all versions, English + German) and, with -Version, the release notes of one
# version, from src/Haulix.App/wwwroot/changelog.json – the same file the in-app "What's new" window uses.
#
#   powershell -ExecutionPolicy Bypass -File tools\release\changelog.ps1
#   powershell -ExecutionPolicy Bypass -File tools\release\changelog.ps1 -Version 0.0.5-beta -NotesFile notes.md
param([string]$Version = "", [string]$NotesFile = "")

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$json = [IO.File]::ReadAllText((Join-Path $root "src\Haulix.App\wwwroot\changelog.json"), [Text.Encoding]::UTF8) | ConvertFrom-Json

function Label($v) { if ($v.tag) { "$($v.version) $($v.tag)" } else { $v.version } }

function Section($v, $lang, $level) {
  $lines = @()
  foreach ($item in $v.$lang) { $lines += "- **$($item[1])** – $($item[2])" }
  return $lines
}

$md = New-Object Text.StringBuilder
[void]$md.AppendLine("# Changelog")
[void]$md.AppendLine("")
[void]$md.AppendLine("All notable changes to HAULIX. The same list appears in the app under *What's new* (Settings → About).")
[void]$md.AppendLine("")
foreach ($v in $json) {
  [void]$md.AppendLine("## $(Label $v)")
  [void]$md.AppendLine("")
  (Section $v "en") | ForEach-Object { [void]$md.AppendLine($_) }
  [void]$md.AppendLine("")
  [void]$md.AppendLine("<details><summary>Deutsch</summary>")
  [void]$md.AppendLine("")
  (Section $v "de") | ForEach-Object { [void]$md.AppendLine($_) }
  [void]$md.AppendLine("")
  [void]$md.AppendLine("</details>")
  [void]$md.AppendLine("")
}
[IO.File]::WriteAllText((Join-Path $root "CHANGELOG.md"), $md.ToString(), (New-Object Text.UTF8Encoding $false))
Write-Host "CHANGELOG.md updated"

if ($Version -and $NotesFile) {
  $plain = ($Version -split '-')[0]
  $v = $json | Where-Object { $_.version -eq $plain } | Select-Object -First 1
  if (-not $v) { throw "Version $plain is not in changelog.json" }
  $n = New-Object Text.StringBuilder
  [void]$n.AppendLine("## What's new in HAULIX $(Label $v)")
  [void]$n.AppendLine("")
  (Section $v "en") | ForEach-Object { [void]$n.AppendLine($_) }
  [void]$n.AppendLine("")
  [void]$n.AppendLine("### Deutsch")
  [void]$n.AppendLine("")
  (Section $v "de") | ForEach-Object { [void]$n.AppendLine($_) }
  [void]$n.AppendLine("")
  [void]$n.AppendLine("---")
  [void]$n.AppendLine("**Install:** download ``Haulix.exe`` below and run it (per user, no admin rights). Existing installations update themselves from inside HAULIX.")
  [void]$n.AppendLine("")
  [void]$n.AppendLine("Windows SmartScreen may warn because the setup is not code-signed: *More info → Run anyway*.")
  [IO.File]::WriteAllText($NotesFile, $n.ToString(), (New-Object Text.UTF8Encoding $false))
  Write-Host "Release notes written to $NotesFile"
}
