# Docket for Android

.NET MAUI app in `src/Docket.Mobile`, sharing `Docket.Core` with the Windows app, CLI and Claude connector, so My Day, repeats, steps and quick add behave the same everywhere.

## Look
Fluent, matching the WinUI 3 app: WinUI's light/dark palette and accent, 4 px controls and 8 px cards, the WinUI type ramp in Selawik (Microsoft's open Segoe UI substitute, OFL), Fluent System Icons (MIT), WinUI-style check boxes, page-coloured top and status bars. Licences are in `Resources/Fonts`.

## Features
Sign in to any Vikunja server (a token is created per phone; the password isn't stored). Home with search, smart lists and counts, lists and groups. Lists: add (quick-add words work), complete, star, swipe right for My Day and left to delete, completed section, sort, rename/delete list, My Day suggestions, Planned grouped by date. Task page: steps, My Day, reminder, due date, repeat (incl. custom), categories, move to list, files (open and add), notes, delete. Reminders become Android notifications, rescheduled each time Docket opens. Light, dark or system theme.

## Build and run
```
. tools\android-env.ps1            # isolated toolchain (tools\android-setup.ps1 installs it; DOCKET_ANDROID_TOOLS sets where)
dotnet build src\Docket.Mobile\Docket.Mobile.csproj -f net10.0-android -t:Install -p:EmbedAssembliesIntoApk=true
```
Emulator: `emulator -avd docket` (a Pixel 7, Android 16 image). The emulator reaches the PC's Vikunja at `http://10.0.2.2:3456`. `tools\adb-ui.ps1` has small helpers for driving the app in tests.

## Your phone
1. Sign in to Tailscale on the PC (tray icon > Log in) and on the phone (Tailscale app, same account).
2. On the PC, run `tailscale serve --bg 3456`, which gives Vikunja an HTTPS address like `https://my-pc.<tailnet>.ts.net`.
3. Install Docket on the phone (USB debugging, or the Play Store internal-testing track) and sign in with that address.
A self-hosted server (see docs/self-hosting.md) works the same way: sign in with its address.

## Play Store checklist
- [ ] Upload key: `keytool -genkeypair -v -keystore docket-upload.keystore -alias docket -keyalg RSA -keysize 2048 -validity 10000` (keep the file and passwords somewhere safe, outside OneDrive's shared folders). Enrol in Play App Signing.
- [ ] Release bundle: `dotnet publish src\Docket.Mobile -f net10.0-android -c Release -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=docket-upload.keystore -p:AndroidSigningKeyAlias=docket -p:AndroidSigningKeyPass=env:DOCKET_KEY_PASS -p:AndroidSigningStorePass=env:DOCKET_STORE_PASS` → `.aab`.
- [ ] Bump `ApplicationVersion` (integer) for every upload; `ApplicationDisplayVersion` for users.
- [ ] Target SDK: the .NET 10 Android workload targets the current API level; check Play's target-level deadline before each release.
- [ ] Permissions to justify in the Play Console: notifications; exact alarms (`USE_EXACT_ALARM` is accepted for apps whose core feature is user-set reminders; otherwise drop it and keep `SCHEDULE_EXACT_ALARM`).
- [ ] Data safety form: the app sends data only to the server the user signs in to; nothing to the developer.
- [ ] Privacy policy URL (required): host one on quartifex.com.
- [ ] Store listing: name, short and full description, 512 px icon (`design/icon`), feature graphic 1024×500, phone screenshots.
- [ ] Check the name "Docket" isn't already taken on Play or trademarked in your category before publishing.
- [ ] Cleartext: `usesCleartextTraffic` is on so home-network http servers work; Play allows it, but HTTPS is recommended to users.
