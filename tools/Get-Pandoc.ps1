<# Downloads the pinned Pandoc into tools\pandoc\ (gitignored). Dev/CI only. #>
. "$PSScriptRoot\_common.ps1"

$Version = '3.5'                                   # keep in sync with ToolVersions.Pandoc
$Url     = "https://github.com/jgm/pandoc/releases/download/$Version/pandoc-$Version-windows-x86_64.zip"
$Sha256  = '887E980E2228D8D45A431936E8BC6270A50CC9A5A4EC7E62064A3A5DE6423B71'

$dest = Join-Path $PSScriptRoot 'pandoc'
$tmp  = Join-Path ([IO.Path]::GetTempPath()) "pandoc-$([guid]::NewGuid().ToString('N')).zip"
try {
    Get-PinnedFile -Url $Url -Sha256 $Sha256 -OutFile $tmp
    if (-not (Test-Path $dest)) {
        New-Item -ItemType Directory -Path $dest | Out-Null
    }
    $extract = Join-Path ([IO.Path]::GetTempPath()) "pandoc-x-$([guid]::NewGuid().ToString('N'))"
    Expand-Archive -Path $tmp -DestinationPath $extract
    Copy-Item (Join-Path $extract "pandoc-$Version\pandoc.exe") $dest -Force
    Remove-Item $extract -Recurse -Force
    & (Join-Path $dest 'pandoc.exe') --version | Select-Object -First 1
} finally { if (Test-Path $tmp) { Remove-Item $tmp -Force } }
