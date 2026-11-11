# Verifies a downloaded file against an XTLS-style release digest sidecar
# (.dgst). The official sidecar format is "<ALGO>= <hex>", one algorithm per
# line — e.g. "SHA2-256= <64 hex chars>" — so the SHA2-256 entry is matched by
# KEY, not by "hash at line start". A file with no parseable SHA2-256 entry is
# a hard failure: verification must never silently pass on a malformed digest.
#
# On mismatch the verified file is DELETED so a corrupt/tampered download can
# never reach extraction. Exits non-zero on every failure path.
#
# Invoked by tools/fetch-assets.ps1 and tested by tools/tests/ (Pester).
param(
    # The downloaded file to verify (e.g. Xray-windows-64.zip).
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    # The release's published digest sidecar (e.g. Xray-windows-64.zip.dgst).
    [Parameter(Mandatory = $true)]
    [string]$DigestPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ZipPath)) {
    throw "File to verify not found: $ZipPath"
}
if (-not (Test-Path -LiteralPath $DigestPath)) {
    throw "Digest file not found: $DigestPath"
}

$actual = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()

# Extract every "SHA2-256= <64-hex>" entry from the sidecar. Key match is
# case-insensitive (PowerShell -match), hex is accepted in either case, and
# surrounding whitespace is tolerated.
$published = Get-Content -LiteralPath $DigestPath |
    ForEach-Object {
        if ($_ -match '^\s*SHA2-256\s*=\s*([0-9a-fA-F]{64})\s*$') {
            $Matches[1].ToLowerInvariant()
        }
    }

if (-not $published -or @($published).Count -eq 0) {
    throw ("No SHA2-256 entry found in the digest file '{0}'. " +
           "Expected at least one line of the form 'SHA2-256= <64 hex chars>'.") -f $DigestPath
}

if (@($published) -notcontains $actual) {
    Remove-Item -LiteralPath $ZipPath -Force -ErrorAction SilentlyContinue
    throw ("SHA256 mismatch: downloaded file hash {0} is not in the release digest file. " +
           "The download was deleted; check your network or pin a known release.") -f $actual
}

Write-Host "SHA256 verified: $actual"
