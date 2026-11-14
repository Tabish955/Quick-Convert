# Shared helpers for the Get-*.ps1 scripts. DEV / CI ONLY. The application itself never downloads anything.
$ErrorActionPreference = 'Stop'
$PlaceholderHash = 'REPLACE_WITH_SHA256'

function Get-PinnedFile {
    param([string]$Url, [string]$Sha256, [string]$OutFile)
    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
    $actual = (Get-FileHash -Algorithm SHA256 -Path $OutFile).Hash.ToUpperInvariant()
    if ($Sha256 -eq $PlaceholderHash) {
        Remove-Item $OutFile -Force
        throw "No SHA256 is pinned yet. Verify the download against the publisher's checksum, then set `$Sha256 in this script to: $actual"
    }
    if ($actual -ne $Sha256.ToUpperInvariant()) {
        Remove-Item $OutFile -Force
        throw "SHA256 mismatch for $Url`n expected $Sha256`n actual   $actual"
    }
    Write-Host "SHA256 OK ($actual)"
}

function Get-ToolsRoot { Join-Path $PSScriptRoot '.' | Resolve-Path | ForEach-Object Path }
