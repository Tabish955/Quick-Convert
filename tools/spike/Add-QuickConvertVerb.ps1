# THROWAWAY spike for decision gate D2 (ROADMAP §4). Run on a dev box, not in CI.
# Answers: where does a classic HKCU verb show up on Windows 11, and what does 20-file
# multi-select cost in processes and popup time?
#
# Install:   powershell -File tools/spike/Add-QuickConvertVerb.ps1
# Uninstall: remove the key it creates:
#   Remove-Item -Recurse "HKCU:\Software\Classes\SystemFileAssociations\.jpg\shell\QuickConvert"

$candidates = @(
    (Join-Path $PSScriptRoot "..\..\src\QuickConvert.UI\bin\Release\net9.0-windows\QuickConvert.UI.exe"),
    (Join-Path $PSScriptRoot "..\..\src\QuickConvert.UI\bin\Debug\net9.0-windows\QuickConvert.UI.exe"),
    "C:\dev\Quick-Convert\src\QuickConvert.UI\bin\Debug\net9.0-windows\QuickConvert.UI.exe"
)

$exe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $exe) { throw "Built exe not found. Looked in: $($candidates -join '; ') - build the UI project first." }

$exe = (Resolve-Path $exe).Path

$verb = "HKCU:\Software\Classes\SystemFileAssociations\.jpg\shell\QuickConvert"

New-Item -Path $verb -Force | Out-Null
Set-ItemProperty -Path $verb -Name "(Default)" -Value "Convert with QuickConvert"

# MultiSelectModel controls whether the verb stays visible for large selections. Microsoft's verb selection
# model table gives legacy command-line verbs a limit of 15 items under "Document" and 100 under "Player",
# so Player is the higher limit. Do not switch to "Document" to raise the limit; it would hide the verb above 15.
# The command passes %1, so Explorer starts one process per selected file; the named pipe and the 150 ms
# debounce in QuickConvert fold those launches into a single batch.
Set-ItemProperty -Path $verb -Name "MultiSelectModel" -Value "Player"

New-Item -Path "$verb\command" -Force | Out-Null
Set-ItemProperty -Path "$verb\command" -Name "(Default)" -Value "`"$exe`" `"%1`""

Write-Host "Verb installed ($exe). Right-click one JPG, then shift-right-click / 'Show more options' to compare placement."
