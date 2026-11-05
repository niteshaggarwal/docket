# Throwaway Vikunja on port 3499 for development. Data in %TEMP%\docket-dev. Never touches C:\dev\vikunja\data.
param([switch]$Reset)
$dir = Join-Path $env:TEMP 'docket-dev'
if ($Reset -and (Test-Path $dir)) { Get-Process vikunja -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir*" } | Stop-Process -Force; Start-Sleep 1; Remove-Item $dir -Recurse -Force }
New-Item -ItemType Directory -Force "$dir\files" | Out-Null
Copy-Item C:\dev\vikunja\bin\vikunja.exe "$dir\vikunja.exe" -Force
@"
service:
  secret: "devsecretdevsecretdevsecret0123456789"
  interface: "127.0.0.1:3499"
  publicurl: "http://localhost:3499/"
  rootpath: "$($dir -replace '\\','/')"
  enableregistration: true
  timezone: "Asia/Kolkata"
database:
  type: "sqlite"
  path: "$($dir -replace '\\','/')/vikunja.db"
files:
  basepath: "$($dir -replace '\\','/')/files"
log:
  standard: "off"
"@ | Set-Content "$dir\config.yml" -Encoding utf8
try { Invoke-RestMethod http://127.0.0.1:3499/api/v1/info | Out-Null } catch {
  # Detached through WMI so it outlives this shell.
  Invoke-CimMethod Win32_Process -MethodName Create -Arguments @{ CommandLine = "`"$dir\vikunja.exe`" --config `"$dir\config.yml`""; CurrentDirectory = $dir } | Out-Null
}
for ($i = 0; $i -lt 30; $i++) { try { Invoke-RestMethod http://127.0.0.1:3499/api/v1/info | Out-Null; break } catch { Start-Sleep 1 } }
$b = 'http://127.0.0.1:3499/api/v1'
try { Invoke-RestMethod "$b/register" -Method Post -ContentType 'application/json' -Body (@{ username='dev'; email='dev@docket.local'; password='devpass1234' } | ConvertTo-Json) | Out-Null } catch {}
& "$dir\vikunja.exe" --config "$dir\config.yml" user set-admin dev 2>&1 | Out-Null
$jwt = (Invoke-RestMethod "$b/login" -Method Post -ContentType 'application/json' -Body (@{ username='dev'; password='devpass1234' } | ConvertTo-Json)).token
$routes = Invoke-RestMethod "$b/routes" -Headers @{ Authorization = "Bearer $jwt" }
$perms = @{}; foreach ($g in $routes.PSObject.Properties) { $perms[$g.Name] = @($g.Value.PSObject.Properties.Name) }
$tok = (Invoke-RestMethod "$b/tokens" -Method Put -Headers @{ Authorization = "Bearer $jwt" } -ContentType 'application/json' -Body (@{ title='dev'; expires_at=(Get-Date).ToUniversalTime().AddDays(30).ToString('yyyy-MM-ddTHH:mm:ssZ'); permissions=$perms } | ConvertTo-Json -Depth 5)).token
Set-Content "$dir\token.txt" $tok -NoNewline
Write-Output "dev server on 3499; token in $dir\token.txt; groups: $($perms.Keys -join ',')"
