# Connects Docket to your Vikunja account. Run it once after creating your account (install.ps1 runs it for you).
#  1. Signs in with your username and password (typed here, never saved or sent anywhere but your Vikunja server)
#  2. Creates two API tokens, saved encrypted (Windows DPAPI) in %LOCALAPPDATA%\Docket\settings.json:
#       "Docket app"          -> the Docket app and docket CLI; can also delete tasks and lists
#       "Docket AI connector" -> docket-mcp, used by AI assistants; cannot delete anything
#  3. Adds the connector to Claude Code and the Claude desktop app, if they're installed
#  4. Optionally closes sign-ups on your Vikunja server and restarts it
# Re-running it is safe: it makes fresh tokens and replaces the saved ones (revoke old ones in Vikunja > Settings > API Tokens).
#
#   powershell -ExecutionPolicy Bypass -File setup-tokens.ps1 [-ServerUrl http://localhost:3456] [-McpExe path\docket-mcp.exe]
#                                       [-VikunjaConfig path\config.yml] [-VikunjaTask "\Docket\Vikunja server"] [-NoCloseSignups]
param(
    [string]$ServerUrl = 'http://localhost:3456',
    [string]$McpExe = (Join-Path $env:LOCALAPPDATA 'Programs\Docket\mcp\docket-mcp.exe'),
    [string]$VikunjaConfig = '',
    [string]$VikunjaTask = '\Docket\Vikunja server',
    [switch]$NoCloseSignups
)
$ErrorActionPreference = 'Stop'
$base = $ServerUrl.TrimEnd('/') + '/api/v1'

$user = Read-Host 'Vikunja username'
$sec  = Read-Host 'Vikunja password' -AsSecureString
$pw   = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
$totp = Read-Host 'Two-factor code (press Enter if you have not turned it on)'

$login = @{ username = $user; password = $pw; long_token = $false }
if ($totp) { $login.totp_passcode = $totp }
$jwt = (Invoke-RestMethod "$base/login" -Method Post -ContentType 'application/json' -Body ($login | ConvertTo-Json)).token
$pw = $null; $login = $null
$h = @{ Authorization = "Bearer $jwt" }

# The Docket app gets everything except webhooks, migrations and CalDAV, including sharing lists and teams.
# The AI token also can't share, manage teams or delete anything.
$routes = Invoke-RestMethod "$base/routes" -Headers $h
$skipGroups = 'caldav','migration','projects_webhooks','webhooks'
$aiSkip = 'projects_shares','projects_users','projects_teams','teams','teams_members'
$aiPerms = @{}; $appPerms = @{}
foreach ($g in $routes.PSObject.Properties) {
    if ($skipGroups -contains $g.Name) { continue }
    $all = @($g.Value.PSObject.Properties.Name)
    $appPerms[$g.Name] = $all
    if ($aiSkip -contains $g.Name) { continue }
    $safe = @($all | Where-Object { $_ -notmatch 'delete' })
    if ($safe.Count) { $aiPerms[$g.Name] = $safe }
}
$expires = (Get-Date).ToUniversalTime().AddYears(1).ToString('yyyy-MM-ddTHH:mm:ssZ')
function New-Token($title, $permissions) {
    $body = @{ title = $title; expires_at = $expires; permissions = $permissions } | ConvertTo-Json -Depth 5
    (Invoke-RestMethod "$base/tokens" -Method Put -Headers $h -ContentType 'application/json' -Body $body).token
}

Add-Type -AssemblyName System.Security
function Protect($s) { [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($s), $null, 'CurrentUser')) }

$dir = Join-Path $env:LOCALAPPDATA 'Docket'
$file = Join-Path $dir 'settings.json'
New-Item -ItemType Directory -Force $dir | Out-Null
$settings = if (Test-Path $file) { Get-Content $file -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
$settings | Add-Member -Force ServerUrl $ServerUrl
if (-not $settings.PSObject.Properties['DefaultProjectId']) { $settings | Add-Member DefaultProjectId 0 }
$settings | Add-Member -Force ProtectedToken (Protect (New-Token 'Docket app' $appPerms))
$settings | Add-Member -Force ClaudeProtectedToken (Protect (New-Token 'Docket AI connector' $aiPerms))
[IO.File]::WriteAllText($file, ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Host 'Docket app and CLI are connected; the AI connector token is saved.' -ForegroundColor Green

if (Test-Path $McpExe) {
    # Claude Code: the Docket connector, available in every project
    if (Get-Command claude -ErrorAction SilentlyContinue) {
        cmd /c "claude mcp remove docket --scope user >nul 2>&1"
        cmd /c "claude mcp add --scope user docket -- `"$McpExe`" >nul 2>&1"
        Write-Host 'Claude Code: "docket" connector added.' -ForegroundColor Green
    }
    # Claude desktop app (the Store version keeps its config under LocalCache)
    $desktopDirs = @(Get-ChildItem "$env:LOCALAPPDATA\Packages" -Directory -Filter 'Claude_*' -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName 'LocalCache\Roaming\Claude' }) + @("$env:APPDATA\Claude")
    $desktopDir = $desktopDirs | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($desktopDir) {
        $cfgPath = Join-Path $desktopDir 'claude_desktop_config.json'
        $cfg = if (Test-Path $cfgPath) { Get-Content $cfgPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
        if (-not $cfg.PSObject.Properties['mcpServers']) { $cfg | Add-Member mcpServers ([pscustomobject]@{}) }
        $cfg.mcpServers | Add-Member -Force docket ([pscustomobject]@{ command = $McpExe; args = @() })
        # Plain UTF-8 without a byte-order mark: the desktop app's JSON reader rejects a BOM.
        [IO.File]::WriteAllText($cfgPath, ($cfg | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
        Write-Host 'Claude desktop app: "docket" connector added. Quit and reopen the app to load it.' -ForegroundColor Green
    }
    Write-Host "Other AI apps (LM Studio, Cursor, VS Code...): add an MCP server with command `"$McpExe`". See docs\ai."
} else {
    Write-Host "Connector not found at $McpExe; skipping AI app registration." -ForegroundColor Yellow
}

if (-not $NoCloseSignups -and $VikunjaConfig -and (Test-Path $VikunjaConfig)) {
    $answer = Read-Host 'Close sign-ups so nobody else can make an account on this server? [Y/n]'
    if ($answer -notmatch '^[nN]') {
        $text = [IO.File]::ReadAllText($VikunjaConfig) -replace 'enableregistration: true', 'enableregistration: false'
        [IO.File]::WriteAllText($VikunjaConfig, $text, [Text.UTF8Encoding]::new($false))
        Get-Process vikunja -ErrorAction SilentlyContinue | Stop-Process -Force
        $tp = Split-Path $VikunjaTask -Parent; if (-not $tp.EndsWith('\')) { $tp += '\' }
        Start-ScheduledTask -TaskPath $tp -TaskName (Split-Path $VikunjaTask -Leaf)
        Start-Sleep 4
        $open = (Invoke-RestMethod "$base/info").auth.local.registration_enabled
        Write-Host "Sign-ups are now $(if ($open) { 'still open (check config.yml)' } else { 'closed' })." -ForegroundColor Green
    }
}
Write-Host 'Tokens are listed in Vikunja under Settings > API Tokens; delete one there to revoke it.'
Write-Host 'Try it:  docket today   (open a new terminal first so PATH picks it up)'
