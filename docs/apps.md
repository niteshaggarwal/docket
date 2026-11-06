# Using Docket

Docket comes as a Windows app, an Android app, a web app and a command line. They all show the same tasks and
behave the same way, because they share one core library: My Day, repeats, quick-add words and subtasks work
identically everywhere.

| | Windows app | Android app | Docket web | `docket` CLI |
|---|---|---|---|---|
| My Day, Important, Planned, All, Completed | ✓ | ✓ | Vikunja's views | ✓ |
| Subtasks, steps, reminders, repeats | ✓ | ✓ | ✓ (Vikunja's) | ✓ |
| Overview, Upcoming, Open tasks | ✓ | ✓ | Upcoming | ✓ |
| List, Table, Gantt and Board views | ✓ | ✓ (timeline for Gantt) | ✓ | Board |
| People, teams and sharing | ✓ | ✓ (new accounts from the PC) | ✓ | ✓ |
| Add-bar commands and AI planning | ✓ | ✓ | | `docket ai` |
| Reminder notifications | ✓ (with `docket notify --watch`) | ✓ | | ✓ |

## Windows app

Start menu > **Docket**. Smart lists, lists and groups, the details pane, boards, people, AI planning and more:
[the Windows app](using/windows.md). Also see [keyboard shortcuts](using/shortcuts.md) and
[quick add and commands](using/quick-add.md).

## Android app

Sign in with your server's address (for a Windows install, use [Tailscale](setup/phone.md#set-it-up)). Swipe right
to add to My Day, left to delete: [the Android app](using/android.md).

## Web app

Open `http://localhost:3457` on the PC with Docket, or your server's address: [Docket web](using/web.md).

## `docket` command line (Windows)

`docket` shows today's tasks; `docket add "Send invoice friday !3"` adds one. Every command, with examples and JSON
output for scripts: [the command line](using/cli.md).

## More

- [Views, boards and list styles](using/views-and-boards.md)
- [People, teams and sharing](using/people-and-sharing.md)
- [AI planning in the app](using/ai-planning.md)
