# Builds the Windows release: the Docket app, the docket CLI, the docket-mcp connector and the docket-web skin,
# self-contained, laid out the way install.ps1 expects (app\, cli\, mcp\ plus the install scripts; web\ is run by hand,
# see docs/self-hosting.md), and optionally zipped.
#   powershell -ExecutionPolicy Bypass -File build\publish-windows.ps1 [-Out artifacts\windows] [-Arch x64|arm64] [-Version 0.3.0] [-Zip]
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\artifacts\windows'),
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [string]$Version = '',
    [switch]$Zip
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Out = [IO.Path]::GetFullPath($Out)
$rid = "win-$Arch"
$platform = if ($Arch -eq 'x64') { 'x64' } else { 'ARM64' }
[string[]]$ver = if ($Version) { "-p:Version=$Version" } else { @() }

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null

function Publish($project, $dest, [string[]]$extra) {
    Write-Host "Publishing $project -> $dest" -ForegroundColor Cyan
    & dotnet publish (Join-Path $repo $project) -c Release -r $rid --self-contained true -o (Join-Path $Out $dest) -p:DebugType=none @ver @extra
    if ($LASTEXITCODE) { throw "dotnet publish failed for $project" }
}

Publish 'src\Docket.App\Docket.App.csproj' 'app' @("-p:Platform=$platform")
Publish 'src\Docket.Cli\Docket.Cli.csproj' 'cli' @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')
Publish 'src\Docket.Mcp\Docket.Mcp.csproj' 'mcp' @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')
# docket-web: Vikunja's web app with the Docket look (a small proxy in front of Vikunja; see docs/self-hosting.md).
Publish 'src\Docket.Web\Docket.Web.csproj' 'web' @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')

Copy-Item (Join-Path $repo 'deploy\windows\*.ps1') $Out
Copy-Item (Join-Path $repo 'LICENSE'), (Join-Path $repo 'NOTICE.md') $Out -ErrorAction SilentlyContinue
Set-Content (Join-Path $Out 'README.txt') -Encoding ascii -Value @"
Docket for Windows
Install: right-click install.ps1 > Run with PowerShell
   or:   powershell -ExecutionPolicy Bypass -File install.ps1
It sets up a private Vikunja server on this PC, the Docket app, the docket command and the AI connector.
Docs: https://github.com/niteshaggarwal/docket
"@

if ($Zip) {
    $name = if ($Version) { "docket-$Version-windows-$Arch.zip" } else { "docket-windows-$Arch.zip" }
    $zipPath = Join-Path (Split-Path $Out -Parent) $name
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $zipPath
    Write-Host "Zipped: $zipPath" -ForegroundColor Green
}
Write-Host "Done: $Out" -ForegroundColor Green
