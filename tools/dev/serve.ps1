# Development-only static file server for previewing the HAULIX UI in a browser (mock backend).
# The real app never uses a server: Haulix.exe loads these files directly through WebView2.
# Usage: powershell -ExecutionPolicy Bypass -File tools/dev/serve.ps1 [-Port 5178]
param([int]$Port = 5178)

$root = (Resolve-Path "$PSScriptRoot/../../src/Haulix.App/wwwroot").Path
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Output "Serving $root on http://localhost:$Port/"

$types = @{
  ".html" = "text/html; charset=utf-8"; ".js" = "text/javascript; charset=utf-8"; ".css" = "text/css; charset=utf-8";
  ".json" = "application/json"; ".svg" = "image/svg+xml"; ".png" = "image/png"; ".woff2" = "font/woff2"; ".ico" = "image/x-icon"
}

while ($listener.IsListening) {
  $ctx = $listener.GetContext()
  try {
    $path = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/'))
    if ($path -eq "") { $path = "index.html" }
    $file = [IO.Path]::GetFullPath((Join-Path $root $path))
    if (-not $file.StartsWith($root) -or -not (Test-Path $file -PathType Leaf)) {
      $ctx.Response.StatusCode = 404
    } else {
      $bytes = [IO.File]::ReadAllBytes($file)
      $ext = [IO.Path]::GetExtension($file).ToLowerInvariant()
      $ctx.Response.ContentType = if ($types.ContainsKey($ext)) { $types[$ext] } else { "application/octet-stream" }
      $ctx.Response.Headers.Add("Cache-Control", "no-store")
      $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    }
  } catch {
    $ctx.Response.StatusCode = 500
  } finally {
    $ctx.Response.Close()
  }
}
