$ErrorActionPreference = 'Stop'
$key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
if (-not (Test-Path -LiteralPath $key)) {
    New-Item -Path $key -Force | Out-Null
}
New-ItemProperty -Path $key -Name 'AllowDevelopmentWithoutDevLicense' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $key -Name 'AllowAllTrustedApps' -Value 1 -PropertyType DWord -Force | Out-Null
Write-Output 'Developer mode enabled.'
