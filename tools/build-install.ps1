$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'StockWidgetNative.csproj'
$manifest = Join-Path $root 'Package.appxmanifest'
$publish = Join-Path $root 'publish'
$stage = Join-Path $env:LOCALAPPDATA 'StockWidgetNative\AppX'
$msix = Join-Path $root 'StockWidgetNative.msix'
$pfx = Join-Path $root 'StockWidgetDev.pfx'
$cer = Join-Path $root 'StockWidgetDev.cer'
$passwordFile = Join-Path $root 'StockWidgetDev.password'

$buildToolsRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
$makeappx = Get-ChildItem -LiteralPath $buildToolsRoot -Recurse -Filter makeappx.exe -File |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$signtool = Get-ChildItem -LiteralPath $buildToolsRoot -Recurse -Filter signtool.exe -File |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $makeappx -or -not $signtool) {
    throw 'Windows SDK BuildTools not found in the NuGet cache. Restore Microsoft.WindowsAppSDK first.'
}

Write-Host 'Publishing...'
& dotnet publish $project -c Release -r win-x64 --self-contained false `
    -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=MSIX -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

Write-Host 'Staging...'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -Path (Join-Path $publish '*') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath $manifest -Destination (Join-Path $stage 'AppxManifest.xml') -Force

if (-not (Test-Path -LiteralPath $pfx)) {
    Write-Host 'Creating development certificate...'
    $password = [Guid]::NewGuid().ToString('N')
    [System.IO.File]::WriteAllText($passwordFile, $password)
    $cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=StockWidgetDev' `
        -KeyUsage DigitalSignature -FriendlyName 'StockWidgetDev' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    $secure = ConvertTo-SecureString -String $password -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
    $elevated = "Import-Certificate -FilePath '$cer' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null"
    Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile', '-Command', $elevated -Wait
} else {
    if (-not (Test-Path -LiteralPath $passwordFile)) {
        throw "Found $pfx but missing $passwordFile. Delete the old PFX and certificate to regenerate, or restore the password file."
    }
    $password = ([System.IO.File]::ReadAllText($passwordFile)).Trim()
}

Write-Host 'Packing...'
if (Test-Path -LiteralPath $msix) { Remove-Item -LiteralPath $msix -Force }
& $makeappx pack /d $stage /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed: $LASTEXITCODE" }

Write-Host 'Signing...'
& $signtool sign /fd SHA256 /f $pfx /p $password /a $msix
if ($LASTEXITCODE -ne 0) { throw "signtool failed: $LASTEXITCODE" }

Write-Host 'Verifying signature...'
& $signtool verify /pa /q $msix
if ($LASTEXITCODE -ne 0) { throw "signature verification failed: $LASTEXITCODE" }

Write-Host 'Installing...'
Get-Process StockWidgetNative -ErrorAction SilentlyContinue | Stop-Process -Force
Get-AppxPackage StockWidgetNative -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
Add-AppxPackage -Path $msix

# The widget host can restart the old provider while the package is being
# replaced. Drop it once more so the next activation loads the new build.
Get-Process StockWidgetNative -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$installed = Get-AppxPackage StockWidgetNative
$builtDll = Join-Path $publish 'StockWidgetNative.dll'
$installedDll = Join-Path $installed.InstallLocation 'StockWidgetNative.dll'
$builtHash = (Get-FileHash -LiteralPath $builtDll -Algorithm SHA256).Hash
$installedHash = (Get-FileHash -LiteralPath $installedDll -Algorithm SHA256).Hash

if ($builtHash -ne $installedHash) {
    throw "Installed DLL does not match the build output. built=$builtHash installed=$installedHash"
}

Write-Host ''
Write-Host 'Build verified:'
$installed | Select-Object Name, Version, PackageFullName, SignatureKind, Status | Format-List
Write-Host ('Package SHA256: ' + (Get-FileHash -LiteralPath $msix -Algorithm SHA256).Hash)
Write-Host ('DLL SHA256:     ' + $builtHash)

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
