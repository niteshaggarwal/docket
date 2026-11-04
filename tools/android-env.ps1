# Dot-source before Android builds:  . tools\android-env.ps1   (toolchain in DOCKET_ANDROID_TOOLS, default: .android-tools next to the repo folder)
$root = if ($env:DOCKET_ANDROID_TOOLS) { $env:DOCKET_ANDROID_TOOLS } else { [IO.Path]::GetFullPath("$PSScriptRoot\..\..\.android-tools") }
$env:DOTNET_ROOT = "$root\dotnet"
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:JAVA_HOME = "$root\jdk-17"
$env:ANDROID_HOME = "$root\android-sdk"
$env:ANDROID_SDK_ROOT = "$root\android-sdk"
$env:PATH = "$root\dotnet;$root\android-sdk\platform-tools;$root\android-sdk\emulator;$root\jdk-17\bin;$env:PATH"
