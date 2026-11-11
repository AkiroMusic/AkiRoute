# Pester tests for tools/Verify-XrayDigest.ps1 — the digest sidecar parsing
# and mismatch behavior must never silently pass: a malformed digest file is a
# hard failure, and a real mismatch deletes the downloaded file.
# Run: Invoke-Pester -Path tools/tests

BeforeAll {
    $script:VerifyScript = Join-Path $PSScriptRoot '..\Verify-XrayDigest.ps1'

    function New-ZipHash {
        (Get-FileHash -Path $script:zip -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

Describe 'Verify-XrayDigest' {
    BeforeEach {
        $script:work = Join-Path $TestDrive ("case-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:work | Out-Null
        $script:zip = Join-Path $script:work 'Xray-windows-64.zip'
        $script:dgst = Join-Path $script:work 'Xray-windows-64.zip.dgst'
        Set-Content -Path $script:zip -Value 'fake-xray-zip-bytes' -NoNewline
    }

    It 'accepts the official sidecar format (SHA2-256 key among other algorithms)' {
        Set-Content -Path $script:dgst -Value (@"
MD5= 402f65a8cccdf123a6c0d5c176ef5252
SHA1= 42e5e9a66b970b5499c34e99d8da6c986c67c2cc
SHA2-256= $(New-ZipHash)
SHA2-512= 5b1356f07a91cbd4fb538fd7eccc494967ea045e770fb60887a9bec0412f0b3bce6148a6514ce7c661824daefe83929b7ffa8b059c22abfc1f1299d9d228712c
"@)
        { & $script:VerifyScript -ZipPath $script:zip -DigestPath $script:dgst } | Should -Not -Throw
        Test-Path $script:zip | Should -BeTrue
    }

    It 'tolerates whitespace around the key and value' {
        Set-Content -Path $script:dgst -Value "  SHA2-256=   $(New-ZipHash)  "
        { & $script:VerifyScript -ZipPath $script:zip -DigestPath $script:dgst } | Should -Not -Throw
    }

    It 'accepts an uppercase hex digest' {
        Set-Content -Path $script:dgst -Value "SHA2-256= $((Get-FileHash -Path $script:zip -Algorithm SHA256).Hash)"
        { & $script:VerifyScript -ZipPath $script:zip -DigestPath $script:dgst } | Should -Not -Throw
    }

    It 'fails loudly when the digest file has no SHA2-256 entry' {
        Set-Content -Path $script:dgst -Value @"
MD5= 402f65a8cccdf123a6c0d5c176ef5252
SHA1= 42e5e9a66b970b5499c34e99d8da6c986c67c2cc
"@
        { & $script:VerifyScript -ZipPath $script:zip -DigestPath $script:dgst } |
            Should -Throw '*No SHA2-256 entry found*'
    }

    It 'fails loudly when the hash has the wrong length' {
        Set-Content -Path $script:dgst -Value 'SHA2-256= abc123'
        { & $script:VerifyScript -ZipPath $script:zip -DigestPath $script:dgst } |
            Should -Throw '*No SHA2-256 entry found*'
    }

    It 'fails and deletes the file on a genuine mismatch' {
        Set-Content -Path $script:dgst -Value ('SHA2-256= ' + ('0' * 64))
        { & $script:VerifyScript -ZipPath $script:zip -DigestPath $script:dgst } |
            Should -Throw '*SHA256 mismatch*'
        Test-Path $script:zip | Should -BeFalse
    }

    It 'fails when the file to verify is missing' {
        Set-Content -Path $script:dgst -Value ('SHA2-256= ' + ('0' * 64))
        Remove-Item $script:zip -Force
        { & $script:VerifyScript -ZipPath $script:zip -DigestPath $script:dgst } |
            Should -Throw '*not found*'
    }

    It 'fails when the digest file is missing' {
        { & $script:VerifyScript -ZipPath $script:zip -DigestPath $script:dgst } |
            Should -Throw '*not found*'
    }
}
