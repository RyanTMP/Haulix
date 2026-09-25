# Copies the logo library from branding/ into the app (src/Haulix.App/wwwroot/assets/logos) and writes an index.
#   branding/truck-brands   -> assets/logos/trucks      (SVG/PNG as they are)
#   branding/trailer-brands -> assets/logos/trailers
#   branding/companies      -> assets/logos/companies   (PNG, scaled down to 240x120 to keep the setup small)
#   branding/cargo          -> assets/logos/cargo
# Run after adding or changing logos:  powershell -ExecutionPolicy Bypass -File tools\brand\sync-logos.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path "$PSScriptRoot\..\.."
$src = Join-Path $root "branding"
$dst = Join-Path $root "src\Haulix.App\wwwroot\assets\logos"

function Reset-Dir($p) { if (Test-Path $p) { Get-ChildItem $p -File | ForEach-Object { [IO.File]::Delete($_.FullName) } } else { New-Item -ItemType Directory -Force $p | Out-Null } }

$index = [ordered]@{ trucks = @(); trailers = @(); companies = [ordered]@{}; cargo = @() }
foreach ($pair in @(@("truck-brands", "trucks"), @("trailer-brands", "trailers"), @("cargo", "cargo"))) {
  $out = Join-Path $dst $pair[1]; Reset-Dir $out
  Get-ChildItem (Join-Path $src $pair[0]) -File | Where-Object { $_.Extension -in ".svg", ".png", ".webp" } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $out $_.Name)
    $index[$pair[1]] += $_.Name
  }
}

$out = Join-Path $dst "companies"; Reset-Dir $out
$names = @{}
Get-Content (Join-Path $src "companies\_index.tsv") -Encoding UTF8 | ForEach-Object { $c = $_ -split "`t"; if ($c.Count -ge 2) { $names[$c[0]] = $c[1] } }
Get-ChildItem (Join-Path $src "companies") -Filter *.png | ForEach-Object {
  $img = [Drawing.Image]::FromFile($_.FullName)
  try {
    $scale = [Math]::Min(1.0, [Math]::Min(240.0 / $img.Width, 120.0 / $img.Height))
    $w = [int][Math]::Max(1, $img.Width * $scale); $h = [int][Math]::Max(1, $img.Height * $scale)
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($img, 0, 0, $w, $h)
    $g.Dispose()
    $bmp.Save((Join-Path $out $_.Name), [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
  } finally { $img.Dispose() }
  $slug = $_.BaseName
  $index.companies[$slug] = $(if ($names[$_.Name]) { $names[$_.Name] } else { $slug })
}

$json = $index | ConvertTo-Json -Depth 4 -Compress
[IO.File]::WriteAllText((Join-Path $dst "index.json"), $json, (New-Object Text.UTF8Encoding($false)))
$size = (Get-ChildItem $dst -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Logos synced: {0} trucks, {1} trailers, {2} companies, {3} cargo icons ({4:N1} MB)" -f $index.trucks.Count, $index.trailers.Count, $index.companies.Count, $index.cargo.Count, $size
