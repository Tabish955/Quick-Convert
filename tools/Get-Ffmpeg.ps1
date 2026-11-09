<#
.SYNOPSIS
    Downloads and installs the pinned FFmpeg 9.0.2 essentials binary bundle for development and CI.

.DESCRIPTION
    Quick-Convert relies on FFmpeg and FFprobe for audio and video conversion.
    This script downloads the pinned Gyan.dev essentials build, verifies its SHA-256 hash,
    and extracts ffmpeg.exe and ffprobe.exe into tools/ffmpeg/bin/.
    
    This script is intended ONLY for developer environment setup and CI pipelines.
    The runtime application itself is strictly offline and never downloads tools.

.PARAMETER Force
    Re-download and overwrite existing binaries even if already present.

.PARAMETER DestinationDir
    Destination directory for ffmpeg.exe and ffprobe.exe.
    Defaults to tools/ffmpeg/bin relative to the script location.
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [string]$DestinationDir
)

$ErrorActionPreference = 'Stop'

$Version = "9.0.2"
$ExpectedHash = "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba"
$PrimaryUrl = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$Version-essentials_build.zip"
$FallbackUrl = "https://github.com/GyanD/codexffmpeg/releases/download/$Version/ffmpeg-$Version-essentials_build.zip"

if ([string]::IsNullOrWhiteSpace($DestinationDir)) {
    $DestinationDir = Join-Path $PSScriptRoot "ffmpeg\bin"
}

$FfmpegExe = Join-Path $DestinationDir "ffmpeg.exe"
$FfprobeExe = Join-Path $DestinationDir "ffprobe.exe"

if (-not $Force -and (Test-Path $FfmpegExe) -and (Test-Path $FfprobeExe)) {
    Write-Host "FFmpeg $Version binaries already exist at: $DestinationDir" -ForegroundColor Green
    Write-Host "Use -Force to reinstall if needed."
    exit 0
}

Write-Host "Setting up FFmpeg $Version into $DestinationDir..." -ForegroundColor Cyan

if (-not (Test-Path $DestinationDir)) {
    New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
}

$TempZip = Join-Path [System.IO.Path]::GetTempPath() "ffmpeg-$Version-essentials-$([Guid]::NewGuid().ToString('N')).zip"
$TempExtract = Join-Path [System.IO.Path]::GetTempPath() "ffmpeg-$Version-extract-$([Guid]::NewGuid().ToString('N'))"

try {
    Write-Host "Downloading FFmpeg $Version..." -ForegroundColor Cyan
    $downloadSuccess = $false

    try {
        Write-Host "Trying primary source: $PrimaryUrl"
        Invoke-WebRequest -Uri $PrimaryUrl -OutFile $TempZip -UseBasicParsing
        $downloadSuccess = $true
    }
    catch {
        Write-Warning "Primary download failed: $_"
        Write-Host "Trying fallback source: $FallbackUrl"
        Invoke-WebRequest -Uri $FallbackUrl -OutFile $TempZip -UseBasicParsing
        $downloadSuccess = $true
    }

    if (-not $downloadSuccess -or -not (Test-Path $TempZip)) {
        throw "Failed to download FFmpeg package from all configured sources."
    }

    Write-Host "Verifying SHA-256 checksum..." -ForegroundColor Cyan
    $actualHash = (Get-FileHash -Path $TempZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $ExpectedHash.ToLowerInvariant()) {
        throw "SHA-256 verification FAILED!`nExpected: $ExpectedHash`nActual:   $actualHash"
    }
    Write-Host "SHA-256 verified successfully ($actualHash)." -ForegroundColor Green

    Write-Host "Extracting binaries..." -ForegroundColor Cyan
    Expand-Archive -Path $TempZip -DestinationPath $TempExtract -Force

    $foundFfmpeg = Get-ChildItem -Path $TempExtract -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
    $foundFfprobe = Get-ChildItem -Path $TempExtract -Recurse -Filter "ffprobe.exe" | Select-Object -First 1

    if (-not $foundFfmpeg -or -not $foundFfprobe) {
        throw "Could not locate ffmpeg.exe or ffprobe.exe inside the extracted archive."
    }

    Copy-Item -Path $foundFfmpeg.FullName -Destination $FfmpegExe -Force
    Copy-Item -Path $foundFfprobe.FullName -Destination $FfprobeExe -Force

    Write-Host "FFmpeg $Version installed successfully to $DestinationDir" -ForegroundColor Green
}
finally {
    if (Test-Path $TempZip) {
        Remove-Item -Path $TempZip -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $TempExtract) {
        Remove-Item -Path $TempExtract -Recurse -Force -ErrorAction SilentlyContinue
    }
}
