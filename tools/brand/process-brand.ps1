# Converts the supplied HAULIX brand images (branding/) into UI-ready assets:
#  - white-on-black logos  -> white with luminance alpha, trimmed
#  - black outline wordmark on white -> white outline with alpha, trimmed
#  - multi-size app.ico from the H logo
# Run from the repo root in Windows PowerShell:  powershell -File tools/brand/process-brand.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class Brand {
  // invert=false: white shapes on dark bg. invert=true: dark shapes on light bg.
  public static Bitmap ToAlpha(Bitmap src, bool invert, int pad) {
    int w = src.Width, h = src.Height;
    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, w, h);
    var d = bmp.LockBits(new Rectangle(0,0,w,h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var px = new byte[d.Stride*h]; Marshal.Copy(d.Scan0, px, 0, px.Length);
    int minX=w, minY=h, maxX=0, maxY=0;
    for (int y=0;y<h;y++) for (int x=0;x<w;x++) {
      int i=y*d.Stride+x*4; int l=(px[i]*29+px[i+1]*150+px[i+2]*77)>>8; if (invert) l=255-l;
      // crush background noise / jpeg haze, stretch the rest
      int a = l < 40 ? 0 : Math.Min(255, (l-40)*255/180);
      px[i]=255; px[i+1]=255; px[i+2]=255; px[i+3]=(byte)a;
      if (a>24) { if(x<minX)minX=x; if(y<minY)minY=y; if(x>maxX)maxX=x; if(y>maxY)maxY=y; }
    }
    Marshal.Copy(px,0,d.Scan0,px.Length); bmp.UnlockBits(d);
    var r = Rectangle.FromLTRB(Math.Max(0,minX-pad), Math.Max(0,minY-pad), Math.Min(w,maxX+pad+1), Math.Min(h,maxY+pad+1));
    var outb = bmp.Clone(r, PixelFormat.Format32bppArgb); bmp.Dispose(); return outb;
  }
  public static Bitmap Square(Bitmap src, int size, float fill) {
    var b = new Bitmap(size,size,PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(b)) {
      g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
      g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
      float s = size*fill / Math.Max(src.Width, src.Height);
      float w = src.Width*s, h = src.Height*s;
      g.DrawImage(src, (size-w)/2f, (size-h)/2f, w, h);
    }
    return b;
  }
  // Rounded dark tile with the H logo — used for the app icon so it reads on light & dark taskbars.
  public static Bitmap IconTile(Bitmap logo, int size) {
    var b = new Bitmap(size,size,PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(b)) {
      g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
      g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
      float r = size*0.22f; var p = new System.Drawing.Drawing2D.GraphicsPath();
      p.AddArc(0,0,r,r,180,90); p.AddArc(size-r-1,0,r,r,270,90); p.AddArc(size-r-1,size-r-1,r,r,0,90); p.AddArc(0,size-r-1,r,r,90,90); p.CloseFigure();
      using (var br = new SolidBrush(Color.FromArgb(255,18,20,24))) g.FillPath(br,p);
      float s = size*0.72f / Math.Max(logo.Width, logo.Height); float w=logo.Width*s, h=logo.Height*s;
      g.DrawImage(logo,(size-w)/2f,(size-h)/2f,w,h);
    }
    return b;
  }
}
"@

$root = (Resolve-Path "$PSScriptRoot/../..").Path
$in = Join-Path $root 'branding'
$out = Join-Path $root 'src/Haulix.App/wwwroot/assets/brand'
New-Item -ItemType Directory -Force $out | Out-Null

$h = [Brand]::ToAlpha([System.Drawing.Bitmap]::FromFile("$in/Haulix_H_Logo.png"), $false, 6)
$h.Save("$out/h-logo.png")
$banner = [Brand]::ToAlpha([System.Drawing.Bitmap]::FromFile("$in/Haulix_ETS2_Logger.png"), $false, 6)
$banner.Save("$out/banner.png")
$word = [Brand]::ToAlpha([System.Drawing.Bitmap]::FromFile("$in/Haulix.png"), $true, 6)
$word.Save("$out/wordmark-outline.png")

# favicon + icon sizes
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
  $tile = [Brand]::IconTile($h, $s)
  $ms = New-Object System.IO.MemoryStream; $tile.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += , $ms.ToArray()
  if ($s -eq 64) { $tile.Save("$out/favicon.png") }
  if ($s -eq 256) { $tile.Save("$out/app-tile.png") }
}
# ICO container with PNG-compressed entries
$icoPath = Join-Path $root 'src/Haulix.App/app.ico'
$fs = [System.IO.File]::Create($icoPath); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Output "Brand assets written to $out and $icoPath"
