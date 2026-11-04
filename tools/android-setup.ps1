# Installs an isolated Android build toolchain for Docket in DOCKET_ANDROID_TOOLS, default .android-tools next to the repo folder (no admin needed):
#   .NET 10 SDK + maui-android workload, Microsoft Build of OpenJDK 17, Android SDK (platform 36, build tools, platform-tools).
# Use it via tools\android-env.ps1 (sets DOTNET_ROOT, PATH, JAVA_HOME, ANDROID_HOME for the current shell).
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = if ($env:DOCKET_ANDROID_TOOLS) { $env:DOCKET_ANDROID_TOOLS } else { [IO.Path]::GetFullPath("$PSScriptRoot\..\..\.android-tools") }
$log = "$root\setup.log"
New-Item -ItemType Directory -Force $root | Out-Null
function Log($m) { $line = "$(Get-Date -Format HH:mm:ss) $m"; Add-Content $log $line; Write-Host $line }

# 1. .NET 10 SDK (user-local)
$dotnet = "$root\dotnet"
if (-not (Test-Path "$dotnet\dotnet.exe")) {
    Log 'Downloading .NET 10 SDK...'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile "$root\dotnet-install.ps1"
    & "$root\dotnet-install.ps1" -Channel 10.0 -InstallDir $dotnet -NoPath | Out-Null
}
$env:DOTNET_ROOT = $dotnet; $env:DOTNET_MULTILEVEL_LOOKUP = '0'; $env:PATH = "$dotnet;$env:PATH"
Log ("dotnet " + (& "$dotnet\dotnet.exe" --version))

# 2. JDK 17
$jdk = "$root\jdk-17"
if (-not (Test-Path "$jdk\bin\java.exe")) {
    Log 'Downloading OpenJDK 17...'
    Invoke-WebRequest 'https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.zip' -OutFile "$root\jdk.zip"
    Expand-Archive "$root\jdk.zip" "$root\jdk-tmp" -Force
    Move-Item (Get-ChildItem "$root\jdk-tmp" -Directory | Select-Object -First 1).FullName $jdk
    Remove-Item "$root\jdk.zip", "$root\jdk-tmp" -Recurse -Force
}
$env:JAVA_HOME = $jdk
Log ((cmd /c "`"$jdk\bin\java.exe`" -version 2>&1" | Select-Object -First 1) -join '')
$ErrorActionPreference = 'Continue'  # native tools write progress to stderr

# 3. MAUI Android workload
Log 'Installing maui-android workload...'
& "$dotnet\dotnet.exe" workload install maui-android --skip-sign-check 2>&1 | Select-Object -Last 3 | ForEach-Object { Log $_ }

# 4. Android SDK via the .NET Android build target
$sdk = "$root\android-sdk"
New-Item -ItemType Directory -Force $sdk | Out-Null
$probe = "$root\probe"
if (-not (Test-Path "$probe\probe.csproj")) {
    & "$dotnet\dotnet.exe" new android -o $probe -n probe 2>&1 | Out-Null
}
Log 'Installing Android SDK packages...'
& "$dotnet\dotnet.exe" build "$probe\probe.csproj" -t:InstallAndroidDependencies -f net10.0-android "-p:AndroidSdkDirectory=$sdk" "-p:JavaSdkDirectory=$jdk" -p:AcceptAndroidSDKLicenses=True 2>&1 | Select-Object -Last 4 | ForEach-Object { Log $_ }
Log ('SDK folders: ' + ((Get-ChildItem $sdk -Directory | Select-Object -ExpandProperty Name) -join ', '))
Log 'DONE'
