$ErrorActionPreference = 'Stop'
$scannerProject = Join-Path $PSScriptRoot 'BleScannerAgent.csproj'
$scannerOutput = Join-Path $PSScriptRoot 'bin\package-win-x64'
$packageDir = Join-Path $PSScriptRoot 'package'
dotnet publish $scannerProject -c Release -r win-x64 --self-contained true -o $scannerOutput
if ($LASTEXITCODE -ne 0) { throw 'Cannot publish Windows BLE scanner.' }
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start-BLE.cmd') -Destination $scannerOutput
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-BLE-Autostart.ps1') -Destination $scannerOutput
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $scannerOutput -Force
$temporaryPackage = Join-Path $packageDir ("BleScannerAgent.$([guid]::NewGuid().ToString('N')).zip")
$packageDestination = Join-Path $packageDir 'BleScannerAgent.zip'
Compress-Archive -Path (Join-Path $scannerOutput '*') -DestinationPath $temporaryPackage
if (Test-Path -LiteralPath $packageDestination) {
    [System.IO.File]::Replace($temporaryPackage, $packageDestination, (Join-Path $packageDir 'BleScannerAgent.previous.zip'))
} else {
    [System.IO.File]::Move($temporaryPackage, $packageDestination)
}
Write-Output 'Windows BLE package ready. Download it from the attendance page to include kiosk configuration.'
dotnet publish (Join-Path $PSScriptRoot 'Installer\BleInstaller.csproj') -c Release -o (Join-Path $PSScriptRoot 'Installer\bin\package')
if ($LASTEXITCODE -ne 0) { throw 'Cannot publish BLE installer.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Installer\bin\package\Retail365-BLE-Setup.exe') -Destination (Join-Path $packageDir 'Retail365-BLE-Setup.exe') -Force
Write-Output 'Single-file BLE installer ready.'
