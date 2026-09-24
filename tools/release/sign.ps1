# Code signing for HAULIX releases (Authenticode, SHA-256, RFC 3161 timestamp).
#
# Works with the Certum "Open Source Code Signing" certificate: with SimplySign Desktop connected, the
# certificate appears in the Windows certificate store (CurrentUser\My) and signtool can use it like a
# smart card. Settings live in tools/release/signing.json (no secrets in there):
#   { "thumbprint": "", "subject": "", "timestamp": "http://time.certum.pl" }
# Leave thumbprint/subject empty to pick the newest valid code-signing certificate automatically.
#
# Dot-source this file, then:  Invoke-HaulixSign -Files @("a.exe", "b.exe") [-Required]
# Without a certificate it warns and leaves the files unsigned (unless -Required).

$ErrorActionPreference = "Stop"
$script:signRoot = (Resolve-Path "$PSScriptRoot\..\..").Path

function Get-HaulixSignTool {
  $cached = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
  if ($cached) { return $cached.FullName }
  $kits = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
  if ($kits) { return $kits.FullName }
  # Fetch Microsoft's official signtool package once (NuGet), no full Windows SDK needed.
  $tmp = Join-Path $env:TEMP "haulix-signtool"
  New-Item -ItemType Directory -Force $tmp | Out-Null
  Set-Content "$tmp\st.csproj" -Encoding UTF8 -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4654" ExcludeAssets="all" /></ItemGroup></Project>'
  dotnet restore "$tmp\st.csproj" -v q --nologo | Out-Null
  $cached = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
  if (-not $cached) { throw "signtool.exe could not be found or downloaded" }
  return $cached.FullName
}

function Get-HaulixSigningCert {
  $cfgPath = Join-Path $PSScriptRoot "signing.json"
  $cfg = if (Test-Path $cfgPath) { Get-Content $cfgPath -Raw | ConvertFrom-Json } else { $null }
  $now = Get-Date
  $certs = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object { $_.NotAfter -gt $now -and $_.NotBefore -lt $now }
  if ($cfg -and $cfg.thumbprint) { $certs = $certs | Where-Object { $_.Thumbprint -eq $cfg.thumbprint.Replace(" ", "").ToUpper() } }
  elseif ($cfg -and $cfg.subject) { $certs = $certs | Where-Object { $_.Subject -like "*$($cfg.subject)*" } }
  $cert = $certs | Sort-Object NotAfter -Descending | Select-Object -First 1
  $ts = if ($cfg -and $cfg.timestamp) { $cfg.timestamp } else { "http://time.certum.pl" }
  return @{ Cert = $cert; Timestamp = $ts }
}

function Invoke-HaulixSign {
  param([string[]]$Files, [switch]$Required)
  $info = Get-HaulixSigningCert
  if (-not $info.Cert) {
    $msg = "No code signing certificate found (Certum: start SimplySign Desktop and log in). Files stay unsigned."
    if ($Required) { throw $msg }
    Write-Warning $msg
    return $false
  }
  $signtool = Get-HaulixSignTool
  Write-Host ("    signing with: {0} (valid until {1:yyyy-MM-dd})" -f $info.Cert.Subject, $info.Cert.NotAfter)
  foreach ($f in $Files) {
    & $signtool sign /sha1 $info.Cert.Thumbprint /fd sha256 /tr $info.Timestamp /td sha256 /d "HAULIX" /du "https://github.com/RyanTMP/Haulix" $f | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $f" }
    & $signtool verify /pa /q $f | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Signature check failed: $f" }
    Write-Host "    signed: $(Split-Path $f -Leaf)"
  }
  return $true
}
