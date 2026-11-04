# Notices

Docket
Copyright (C) 2026 Quartifex

This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General
Public License as published by the Free Software Foundation, version 3 (see [LICENSE](LICENSE)). It is distributed
in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
FITNESS FOR A PARTICULAR PURPOSE.

If you run a modified version of Docket for other people over a network (for example a hosted connector), the AGPL
requires you to offer them the source of your version.

## Third-party software

Docket works with, and the installers download, software that keeps its own licence:

| Component | Licence | Notes |
|---|---|---|
| [Vikunja](https://vikunja.io) | AGPL-3.0 | The task server. Downloaded by the installers; not included in this repository |
| [Caddy](https://caddyserver.com) | Apache-2.0 | Reverse proxy in the Docker stack (official image) |
| [.NET](https://dotnet.microsoft.com) runtime and libraries | MIT | Bundled in self-contained builds |
| [Windows App SDK / WinUI 3](https://github.com/microsoft/WindowsAppSDK) | MIT | Windows app |
| [.NET MAUI](https://github.com/dotnet/maui) | MIT | Android app |
| [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) | Apache-2.0 | Connector |
| [CommunityToolkit.Maui](https://github.com/CommunityToolkit/Maui), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), [CommunityToolkit.WinUI.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit) | MIT | |
| [Plugin.LocalNotification](https://github.com/thudugala/Plugin.LocalNotification) | MIT | Android reminders |
| [xUnit](https://xunit.net) | Apache-2.0 | Tests only |

## Fonts and icons

| Asset | Licence | File |
|---|---|---|
| Selawik (Microsoft) | SIL Open Font License 1.1 | `src/Docket.Mobile/Resources/Fonts/Selawik-LICENSE.txt` |
| Fluent UI System Icons (Microsoft) | MIT | `src/Docket.Mobile/Resources/Fonts/FluentSystemIcons-LICENSE.txt` |
| Docket app icon ("Done stack") | AGPL-3.0, as part of Docket | `design/icon` |

## Trademarks

"Docket" and the Docket icon identify this project. Vikunja, Microsoft, Windows, Claude, ChatGPT, Ollama, LM Studio,
Tailscale and other names are trademarks of their owners; Docket is not affiliated with or endorsed by them.
