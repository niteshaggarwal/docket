# Runs the Release docket CLI against the throwaway dev server (tools/devserver.ps1).
Add-Type -AssemblyName System.Security
$dir = Join-Path $env:TEMP 'docket-dev'
$file = Join-Path $dir 'cli-settings.json'
$tok = Get-Content "$dir\token.txt" -Raw
$prot = [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($tok), $null, 'CurrentUser'))
@{ ServerUrl = 'http://127.0.0.1:3499'; ProtectedToken = $prot; VikunjaExe = "$dir\vikunja.exe" } | ConvertTo-Json | Set-Content $file -Encoding utf8
$env:DOCKET_SETTINGS = $file
$exe = Get-ChildItem C:\dev\docket\src\Docket.Cli\bin\Release -Recurse -Filter docket.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1
& $exe.FullName status | Select-String '3499' -Quiet | ForEach-Object { if (-not $_) { throw "Refusing to run: the CLI isn't pointed at the dev server." } }
& $exe.FullName @args
