$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$startupFolder = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupFolder 'Retail365 BLE Scanner.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $env:ComSpec
$shortcut.Arguments = "/c `"$(Join-Path $packageRoot 'Start-BLE.cmd')`""
$shortcut.WorkingDirectory = $packageRoot
$shortcut.WindowStyle = 7
$shortcut.Description = 'Start Retail365 BLE store kiosk after Windows sign-in'
$shortcut.Save()
Write-Output 'Retail365 BLE scanner will start when this Windows account signs in.'
Write-Output 'Keep the kiosk computer awake and signed in. Remove the Startup shortcut to disable auto-start.'
