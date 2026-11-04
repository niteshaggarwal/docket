# Docket one-click installer for Windows 10/11. No admin rights needed; everything installs for the current user.
#
#   Right-click install.ps1 > Run with PowerShell,  or:  powershell -ExecutionPolicy Bypass -File install.ps1
#
# What it does (each step is skipped if already done, so re-running it upgrades in place):
#   1. Downloads Vikunja (the open-source task server Docket uses) and sets it up on 127.0.0.1 only
#   2. Installs the Docket app, the `docket` command (added to your PATH) and the docket-mcp AI connector
#   3. Adds scheduled tasks: start Vikunja when you sign in, and a nightly backup (newest 14 kept)
#   4. Opens Vikunja so you can create your account, then connects the app, CLI and AI apps to it
# Optional: -Reminders adds a task that shows reminder notifications from sign-in. -NoScheduledTasks and -NoPath give a
# portable setup (start Vikunja yourself with: vikunja.exe --config config.yml).
#
# Data lives outside OneDrive (a live SQLite database and sync don't mix); backups go to Documents\Docket\backups,
# which OneDrive may back up. Remove everything with uninstall.ps1.
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Docket'),
    [string]$ServerDir = (Join-Path $env:LOCALAPPDATA 'Docket\server'),
    [string]$BackupDir = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Docket\backups'),
    [int]$Port = 3456,
    [string]$VikunjaVersion = 'latest',
    [string]$TimeZone = '',
    [switch]$Reminders,
    [switch]$NoStartMenu,
    [switch]$NoPath,
    [switch]$NoScheduledTasks,
    [switch]$SkipAccountSetup,
    [string]$Repo = 'quartifex/docket'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$utf8 = [Text.UTF8Encoding]::new($false)
$taskPath = '\Docket\'
function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

if ([Environment]::OSVersion.Version.Build -lt 17763) { throw 'Docket needs Windows 10 version 1809 or newer.' }
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }

# ---- 1. Vikunja ---------------------------------------------------------------------------------
Step 'Vikunja server'
$bin = Join-Path $ServerDir 'bin'
$exe = Join-Path $bin 'vikunja.exe'
$config = Join-Path $ServerDir 'config.yml'
New-Item -ItemType Directory -Force $bin, (Join-Path $ServerDir 'data\files'), (Join-Path $ServerDir 'logs'), $BackupDir | Out-Null

$api = if ($VikunjaVersion -eq 'latest') { 'https://api.github.com/repos/go-vikunja/vikunja/releases/latest' }
       else { "https://api.github.com/repos/go-vikunja/vikunja/releases/tags/v$($VikunjaVersion.TrimStart('v'))" }
$release = Invoke-RestMethod $api -Headers @{ 'User-Agent' = 'docket-installer' }
$vikArch = if ($arch -eq 'arm64') { 'arm64' } else { 'amd64' }
$asset = $release.assets | Where-Object { $_.name -match "^vikunja-v[\d.]+-windows-4\.0-$vikArch\.exe-full\.zip$" } | Select-Object -First 1
if (-not $asset) { throw "No Windows $vikArch download in Vikunja $($release.tag_name)." }
$current = if (Test-Path $exe) { (& $exe version 2>$null | Out-String) } else { '' }
if ($current -notmatch [regex]::Escape($release.tag_name.TrimStart('v'))) {
    Write-Host "Downloading Vikunja $($release.tag_name)..."
    $zip = Join-Path $env:TEMP $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
    $sha = $release.assets | Where-Object { $_.name -eq "$($asset.name).sha256" } | Select-Object -First 1
    if ($sha) {
        $expected = ((Invoke-RestMethod $sha.browser_download_url -Headers @{ 'User-Agent' = 'docket-installer' }) -split '\s+')[0]
        $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
        if ($expected -and $actual -ne $expected.ToUpperInvariant()) { throw "Checksum mismatch for $($asset.name)." }
    }
    $tmp = Join-Path $env:TEMP 'docket-vikunja'
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive $zip $tmp
    Get-Process vikunja -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
    Copy-Item (Get-ChildItem $tmp -Filter 'vikunja-*.exe' -Recurse | Select-Object -First 1).FullName $exe -Force
    Remove-Item $tmp, $zip -Recurse -Force
}

if (-not (Test-Path $config)) {
    if (-not $TimeZone) {
        # PowerShell 7 can map the Windows zone to the IANA name Vikunja wants; otherwise ask.
        $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
        if ($pwsh) { $TimeZone = (& $pwsh.Source -NoProfile -Command '$z=$null; if ([TimeZoneInfo]::TryConvertWindowsIdToIanaId([TimeZoneInfo]::Local.Id, [ref]$z)) { $z }') }
        if (-not $TimeZone) { $TimeZone = Read-Host 'Your time zone, e.g. Europe/London, America/New_York, Asia/Kolkata' }
    }
    $secret = -join ((1..48) | ForEach-Object { '{0:x2}' -f (Get-Random -Minimum 0 -Maximum 256) })
    $root = $ServerDir.Replace('\', '/')
    [IO.File]::WriteAllText($config, @"
service:
  secret: "$secret"
  interface: "127.0.0.1:$Port"
  publicurl: "http://localhost:$Port/"
  rootpath: "$root"
  enableregistration: true
  timezone: "$TimeZone"
database:
  type: "sqlite"
  path: "$root/data/vikunja.db"
files:
  basepath: "$root/data/files"
log:
  path: "$root/logs"
  standard: "file"
  level: "INFO"
"@, $utf8)
    Write-Host "Config written: $config (listens on 127.0.0.1:$Port only)"
}

$backup = Join-Path $ServerDir 'backup.ps1'
[IO.File]::WriteAllText($backup, @"
# Nightly Docket backup: a consistent Vikunja dump into $BackupDir, keeping the newest 14.
& '$exe' --config '$config' dump --path '$BackupDir'
Get-ChildItem '$BackupDir' -Filter 'vikunja-dump_*.zip' | Sort-Object LastWriteTime -Descending | Select-Object -Skip 14 | Remove-Item
"@, $utf8)

# ---- 2. Docket app, CLI, connector --------------------------------------------------------------
Step 'Docket app, CLI and AI connector'
$payload = $PSScriptRoot
if (-not (Test-Path (Join-Path $payload 'app\Docket.exe'))) {
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    if ((Test-Path (Join-Path $repoRoot 'src\Docket.Core')) -and (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host 'Building from source (a few minutes)...'
        $payload = Join-Path $env:TEMP 'docket-build'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'build\publish-windows.ps1') -Out $payload -Arch $arch
        if ($LASTEXITCODE) { throw 'Build failed.' }
    } else {
        Write-Host "Downloading the latest Docket release from github.com/$Repo..."
        $rel = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'docket-installer' }
        $a = $rel.assets | Where-Object { $_.name -match "windows-$arch\.zip$" } | Select-Object -First 1
        if (-not $a) { throw "No Windows $arch download in $Repo $($rel.tag_name)." }
        $zip = Join-Path $env:TEMP $a.name
        Invoke-WebRequest $a.browser_download_url -OutFile $zip -UseBasicParsing
        $payload = Join-Path $env:TEMP 'docket-release'
        if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
        Expand-Archive $zip $payload
        Remove-Item $zip
    }
}
Get-Process Docket, docket, docket-mcp -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir*" } | Stop-Process -Force
foreach ($part in 'app', 'cli', 'mcp') {
    $dest = Join-Path $InstallDir $part
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Copy-Item (Join-Path $payload $part) $dest -Recurse
}
Copy-Item (Join-Path $PSScriptRoot '*.ps1') $InstallDir -Force -ErrorAction SilentlyContinue
$cliDir = Join-Path $InstallDir 'cli'
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (-not $NoPath -and ($userPath -split ';') -notcontains $cliDir) {
    [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ";$cliDir").TrimStart(';'), 'User')
    Write-Host "Added $cliDir to your PATH (new terminals pick it up)."
}
if (-not $NoStartMenu) {
    $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Docket.lnk'
    $sc = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
    $sc.TargetPath = Join-Path $InstallDir 'app\Docket.exe'
    $sc.WorkingDirectory = Join-Path $InstallDir 'app'
    $sc.Description = 'Docket tasks'
    $sc.Save()
    Write-Host 'Start menu shortcut: Docket'
}

# ---- 3. Scheduled tasks -------------------------------------------------------------------------
if ($NoScheduledTasks) {
    Step 'Starting Vikunja (no scheduled tasks)'
    Start-Process conhost.exe -ArgumentList "--headless `"$exe`" --config `"$config`"" -WorkingDirectory $ServerDir -WindowStyle Hidden
} else {
Step 'Scheduled tasks'
$me = "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId $me -LogonType Interactive -RunLevel Limited
function Register($name, $action, $trigger, $what) {
    Register-ScheduledTask -TaskPath $taskPath -TaskName $name -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
    Write-Host "$name - $what"
}
Register 'Vikunja server' (New-ScheduledTaskAction -Execute 'conhost.exe' -Argument "--headless `"$exe`" --config `"$config`"" -WorkingDirectory $ServerDir) `
    (New-ScheduledTaskTrigger -AtLogOn -User $me) 'starts the task server when you sign in'
Register 'Vikunja backup' (New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$backup`"") `
    (New-ScheduledTaskTrigger -Daily -At 11pm) "nightly backup to $BackupDir"
if ($Reminders) {
    Register 'Docket reminders' (New-ScheduledTaskAction -Execute 'conhost.exe' -Argument "--headless `"$cliDir\docket.exe`" notify --watch") `
        (New-ScheduledTaskTrigger -AtLogOn -User $me) 'reminder notifications from sign-in'
}

Start-ScheduledTask -TaskPath $taskPath -TaskName 'Vikunja server'
}
$up = $false
foreach ($i in 1..20) { try { Invoke-RestMethod "http://127.0.0.1:$Port/api/v1/info" | Out-Null; $up = $true; break } catch { Start-Sleep 1 } }
if (-not $up) { throw "Vikunja didn't start. Check $ServerDir\logs." }
Write-Host "Vikunja is running at http://localhost:$Port" -ForegroundColor Green
if ($Reminders -and -not $NoScheduledTasks) { Start-ScheduledTask -TaskPath $taskPath -TaskName 'Docket reminders' }

# ---- 4. Account and tokens ----------------------------------------------------------------------
if (-not $SkipAccountSetup) {
    Step 'Your account'
    $settingsFile = Join-Path $env:LOCALAPPDATA 'Docket\settings.json'
    $redo = 'y'
    if (Test-Path $settingsFile) { $redo = Read-Host 'Docket is already connected to an account. Connect again (new tokens)? [y/N]' }
    if ($redo -match '^[yY]') {
        Write-Host "Opening http://localhost:$Port - create your account there (or sign in), then come back here."
        Start-Process "http://localhost:$Port/register"
        Read-Host 'Press Enter when your account exists'
        & (Join-Path $InstallDir 'setup-tokens.ps1') -ServerUrl "http://localhost:$Port" -McpExe (Join-Path $InstallDir 'mcp\docket-mcp.exe') `
            -VikunjaConfig $config -VikunjaTask "$($taskPath)Vikunja server"
    }
}

Step 'Done'
Write-Host "App:        Start menu > Docket   ($InstallDir\app\Docket.exe)"
Write-Host "CLI:        docket today   (in a new terminal)"
Write-Host "AI:         $InstallDir\mcp\docket-mcp.exe  - see https://github.com/$Repo/tree/main/docs/ai"
Write-Host "Data:       $ServerDir     Backups: $BackupDir"
Write-Host "Phone:      see https://github.com/$Repo/blob/main/docs/self-hosting.md#reach-it-from-your-phone"
Write-Host "Uninstall:  powershell -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
