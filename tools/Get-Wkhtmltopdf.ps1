<# Downloads the pinned wkhtmltopdf into tools\wkhtmltopdf\ (gitignored). Dev/CI only.
   Upstream ships Windows builds only as an NSIS installer. It is run silently into a temp folder and only
   wkhtmltopdf.exe (+ its DLL) is copied out. LGPL: shipped as a separate program, never linked in-process. #>
. "$PSScriptRoot\_common.ps1"

$Version = '0.12.6-1'                               # keep in sync with ToolVersions.Wkhtmltopdf
$Url     = "https://github.com/wkhtmltopdf/packaging/releases/download/$Version/wkhtmltox-$Version.msvc2015-win64.exe"
$Sha256  = '1EFCFAF8472871268FD399865A3E62B435B91D4413009B80830EAAFF7A57D088'

$dest = Join-Path $PSScriptRoot 'wkhtmltopdf'
$tmp  = Join-Path ([IO.Path]::GetTempPath()) "wkhtmltox-$([guid]::NewGuid().ToString('N')).exe"
$inst = Join-Path ([IO.Path]::GetTempPath()) "wkhtmltox-i-$([guid]::NewGuid().ToString('N'))"
try {
    Get-PinnedFile -Url $Url -Sha256 $Sha256 -OutFile $tmp
    Start-Process -FilePath $tmp -ArgumentList '/S', "/D=$inst" -Wait
    if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }
    Copy-Item (Join-Path $inst 'bin\*') $dest -Recurse -Force
    & (Join-Path $dest 'wkhtmltopdf.exe') --version
} finally {
    if (Test-Path $tmp)  { Remove-Item $tmp -Force }
    if (Test-Path $inst) { Remove-Item $inst -Recurse -Force -ErrorAction SilentlyContinue }
}
