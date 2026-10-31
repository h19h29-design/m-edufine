$ErrorActionPreference = 'Stop'
foreach ($browser in @('Microsoft\Edge', 'Google\Chrome')) {
    $key = "HKCU:\Software\$browser\NativeMessagingHosts\com.medufine.bridge"
    if (Test-Path $key) { Remove-Item -Path $key }
}
Write-Host 'Only M-Edufine native messaging registration was removed. Business data and browser profiles were not touched.'
