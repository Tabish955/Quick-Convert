<# Downloads the pinned 7za.exe into tools\7z\ (gitignored). Dev/CI only. Used ONLY to create .7z archives.
   Needs two pinned files: the standalone 7zr.exe (to unpack the "extra" package) and the extra package. #>
. "$PSScriptRoot\_common.ps1"

$Version   = '23.01'                               # keep in sync with ToolVersions.SevenZip
$Base      = "https://github.com/ip7z/7zip/releases/download/$Version"
$ZrUrl     = "$Base/7zr.exe";            $ZrSha    = '72C98287B2E8F85EA7BB87834B6CE1CE7CE7F41A8C97A81B307D4D4BF900922B'
$ExtraUrl  = "$Base/7z2301-extra.7z";    $ExtraSha = 'DB3A1CBE57A26FAC81B65C6A2D23FEAECDEEDE3E4C1FE8FB93A7B91D72D1094C'

$dest = Join-Path $PSScriptRoot '7z'
$work = Join-Path ([IO.Path]::GetTempPath()) "7z-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $zr = Join-Path $work '7zr.exe'; $extra = Join-Path $work 'extra.7z'
    Get-PinnedFile -Url $ZrUrl    -Sha256 $ZrSha    -OutFile $zr
    Get-PinnedFile -Url $ExtraUrl -Sha256 $ExtraSha -OutFile $extra
    & $zr x $extra "-o$work\x" -y | Out-Null
    if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }
    Copy-Item "$work\x\x64\7za.exe" $dest -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path "$dest\7za.exe")) { Copy-Item "$work\x\7za.exe" $dest -Force }
    & (Join-Path $dest '7za.exe') | Select-Object -First 2
} finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
