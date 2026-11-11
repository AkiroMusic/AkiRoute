# Downloads the xray-core engine assets into Akiroute/Assets (engine + rules).
# Source: official XTLS/Xray-core GitHub releases (Xray-windows-64.zip).
#
# Integrity: the release's published digest file (Xray-windows-64.zip.dgst) is
# downloaded alongside the zip and the computed SHA256 must match one of its
# entries — a corrupted or tampered download aborts the script instead of
# shipping a broken engine. Pin $XrayVersion to a fixed release for reproducible
# builds; 'latest' always re-verifies the digest but tracks upstream.
#
# Idempotent: skips re-download when the zip already exists and its hash matches
# the digest file.
param(
    # Pinned xray-core release tag. This is the version the app's protocol
    # mappings were verified against (see XrayConfigValidationTests); bump it
    # ONLY together with digest verification and an engine validation run.
    # Pass -XrayVersion latest to explicitly track upstream.
    [string]$XrayVersion = 'v26.3.27'
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$engine  = Join-Path $root 'Akiroute\Assets\engine'
$rules   = Join-Path $root 'Akiroute\Assets\rules'
$extract = Join-Path $PSScriptRoot 'xray-extract'
$zip     = Join-Path $PSScriptRoot 'xray-windows-64.zip'
$dgst    = Join-Path $PSScriptRoot 'xray-windows-64.zip.dgst'

New-Item -ItemType Directory -Force -Path $engine, $rules | Out-Null

$base = if ($XrayVersion -eq 'latest') {
    'https://github.com/XTLS/Xray-core/releases/latest/download'
} else {
    "https://github.com/XTLS/Xray-core/releases/download/$XrayVersion"
}

if (-not (Test-Path $zip) -or (Get-Item $zip).Length -lt 1000000) {
    $zipUrl = "$base/Xray-windows-64.zip"
    Write-Host "Downloading $zipUrl ..."
    Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing
} else {
    Write-Host "Zip already present, reusing: $zip"
}

# --- Integrity verification against the release's published digest file. ---
# The .dgst sidecar is "ALGO= <hex>" per line, so the SHA2-256 entry must be
# matched by key — delegated to Verify-XrayDigest.ps1 (unit-tested in
# tools/tests/), which also deletes the zip and throws on any mismatch.
Write-Host "Downloading digest file for verification ..."
Invoke-WebRequest -Uri "$base/Xray-windows-64.zip.dgst" -OutFile $dgst -UseBasicParsing

& (Join-Path $PSScriptRoot 'Verify-XrayDigest.ps1') -ZipPath $zip -DigestPath $dgst

Write-Host 'Extracting ...'
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $extract -Force

$map = @{
    'xray.exe'    = $engine
    'wintun.dll'  = $engine
    'geoip.dat'   = $rules
    'geosite.dat' = $rules
}
$missing = @()
foreach ($name in $map.Keys) {
    $srcFile = Join-Path $extract $name
    if (Test-Path $srcFile) {
        Copy-Item $srcFile (Join-Path $map[$name] $name) -Force
        Write-Host ("OK  " + (Join-Path $map[$name] $name) + "  " + (Get-Item $srcFile).Length + " bytes")
    } else {
        $missing += $name
    }
}

# A build without a required asset produces a broken app; fail loudly.
if ($missing.Count -gt 0) {
    Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue
    throw ("Required asset(s) missing from the archive: " + ($missing -join ', '))
}

# Cleanup extraction dir (keep the zip + digest for idempotent re-runs)
Remove-Item $extract -Recurse -Force
Write-Host 'Done.'
