# Removes Docket installed by install.ps1: scheduled tasks, programs, PATH entry and Start menu shortcut.
# Your tasks (the server data) and backups are kept unless you pass -RemoveData.
#   powershell -ExecutionPolicy Bypass -File uninstall.ps1 [-RemoveData]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Docket'),
    [string]$ServerDir = (Join-Path $env:LOCALAPPDATA 'Docket\server'),
    [switch]$RemoveData
)
$ErrorActionPreference = 'Stop'

foreach ($t in Get-ScheduledTask -TaskPath '\Docket\' -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskPath '\Docket\' -TaskName $t.TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskPath '\Docket\' -TaskName $t.TaskName -Confirm:$false
    Write-Host "Removed scheduled task $($t.TaskName)"
}
Get-Process vikunja, Docket, docket, docket-mcp -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$InstallDir*" -or $_.Path -like "$ServerDir*" } | Stop-Process -Force

$cliDir = Join-Path $InstallDir 'cli'
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
[Environment]::SetEnvironmentVariable('Path', (($userPath -split ';') | Where-Object { $_ -and $_ -ne $cliDir }) -join ';', 'User')
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Docket.lnk') -ErrorAction SilentlyContinue

if (Get-Command claude -ErrorAction SilentlyContinue) { cmd /c "claude mcp remove docket --scope user >nul 2>&1" }
Write-Host 'If you added Docket to the Claude desktop app or another AI app, remove "docket" from its MCP settings.'

# Leave this script's own folder until last (it may be running from there).
foreach ($p in 'app', 'cli', 'mcp') { Remove-Item (Join-Path $InstallDir $p) -Recurse -Force -ErrorAction SilentlyContinue }
if ($RemoveData) {
    Remove-Item $ServerDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:LOCALAPPDATA 'Docket\settings.json') -ErrorAction SilentlyContinue
    Write-Host "Removed server data in $ServerDir (backups in Documents\Docket\backups are kept)."
} else {
    Write-Host "Kept your data in $ServerDir. Re-run with -RemoveData to delete it."
}
Write-Host 'Docket is uninstalled.' -ForegroundColor Green
