# Portable package only. The setup executable registers the bridge automatically.
$ErrorActionPreference = 'Stop'
$base = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $base 'native-host.json'
if (-not (Test-Path (Join-Path $base 'M-Edufine.exe'))) { throw 'Extract the entire portable ZIP before running this script.' }
foreach ($browser in @('Microsoft\Edge', 'Google\Chrome')) {
    $key = "HKCU:\Software\$browser\NativeMessagingHosts\com.medufine.bridge"
    New-Item -Path $key -Force | Out-Null
    Set-Item -Path $key -Value $manifest
}
Write-Host 'Bridge registered for the current Windows user. Load the extension folder in Edge, then connect the work tab.'
