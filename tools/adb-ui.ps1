# Helpers to drive the Android app on an emulator for testing. Dot-source after android-env.ps1.
function Ui-Dump {
    adb shell uiautomator dump /sdcard/ui.xml 2>$null | Out-Null
    [xml](adb exec-out cat /sdcard/ui.xml)
}
# Finds a node by text, hint or description; text boxes (EditText) win over labels with the same words.
function Ui-Find([string]$text) {
    $nodes = (Ui-Dump).SelectNodes('//node') | Where-Object { $_.text -eq $text -or $_.'content-desc' -eq $text -or $_.hint -eq $text }
    $edit = $nodes | Where-Object class -eq 'android.widget.EditText' | Select-Object -First 1
    if ($edit) { $edit } else { $nodes | Select-Object -First 1 }
}
function Ui-Tap([string]$text) {
    $n = Ui-Find $text
    if (-not $n) { throw "Not on screen: $text" }
    $b = [regex]::Matches($n.bounds, '\d+') | ForEach-Object { [int]$_.Value }
    adb shell input tap ([int](($b[0] + $b[2]) / 2)) ([int](($b[1] + $b[3]) / 2))
    Start-Sleep -Milliseconds 700
}
function Ui-Type([string]$text) { adb shell input text ($text -replace ' ', '%s'); Start-Sleep -Milliseconds 300 }
function Ui-Texts { ((Ui-Dump).SelectNodes('//node') | Where-Object { $_.text } | Select-Object -ExpandProperty text) -join ' | ' }
