# Using the Docket apps

## Windows app

Start menu > **Docket** (installed by `install.ps1`).

- **Smart lists:** My Day (resets each day; **Suggestions** offers overdue, due and recent tasks), Important, Planned
  (grouped Earlier, Today, Tomorrow, This week, Later), All, Completed, and Tasks. Hide any of them in Settings.
- **Lists and groups** (Vikunja parent projects): new, rename, colour, move into a group, delete. Drag a task onto a
  list to move it; drag to reorder in "My order".
- **Task details:** subtasks (nested, any depth; a task can have several parents), parents, assignees, priority,
  start and end dates, My Day, reminder, due date, repeat (daily, weekdays, weekly, monthly, yearly, custom),
  categories (coloured labels), files, list, notes, history, and delete with Undo.
- **Views:** sort by my order, importance, due date, A–Z or creation date; show or hide completed; copy a list as
  text; search; List, Table, Gantt and Board views per list; light, dark or system theme.
- **Overview, Upcoming and Open tasks** sit with the smart lists. Overview is a dashboard: counts, the last 14 days,
  priorities, today, overdue, upcoming, assigned to you and progress per list.
- **Boards:** add columns, rename them, give them a colour and an icon (Fluent glyph or emoji). To-Do and Done stay.
- **List options:** icon, background (colour, gradient, accent or image), share (people, teams, public links).
- **Add bar** (at the bottom): type a task with quick-add words, or a command: `/s` search, `/x` mark the matches done,
  `/m` add them to My Day, `/i` star them, `/ai` plan with AI. Turn on the sparkle to send everything to the AI.
- **AI** (Settings > AI): Local (Ollama on this PC, using a model you have installed), Claude, ChatGPT or Gemini with
  your own API key (stored encrypted for your Windows account), or Off.
- **People and teams:** add people by email or as local people (they get a Docket account on your server; you hand
  them the password shown once), make teams, and assign tasks.
- **Open in browser** opens the Docket web app (`docket-web`, http://localhost:3457) when it runs, else the server.
- **Shortcuts:** Ctrl+N add · Ctrl+D complete · Ctrl+T My Day · Ctrl+I important · Delete · Enter details ·
  Esc close · Ctrl+F search · Ctrl+L new list · Ctrl+R or F5 refresh.
- **Server stopped?** If your task server on this PC isn't running, the app starts it and carries on (you'll see
  "Starting your task server…"). See [if the server stops](self-hosting.md#if-the-server-stops).
- **Reminders** pop up through `docket notify --watch` (the installer's `-Reminders` option starts it at sign-in).
- Weekdays, yearly and every-N-months/years repeats are handled by Docket (a "↻" label), so they advance when
  completed in Docket, the CLI, Android or an AI, but not in Vikunja's own web app.

## Android app

Sign in with your server's address (for a Windows install, use Tailscale; see
[phone access](self-hosting.md#reach-it-from-your-phone)). Docket makes a token for the phone; your password isn't stored.
Swipe right to add to My Day, left to delete. The Android app has the same subtasks, people, sharing, boards,
list styles, views, add-bar commands and AI as Windows (accounts for new people are made from the PC). Reminders become Android notifications. Build notes: [ANDROID.md](../ANDROID.md).

## `docket` command line (Windows)

```
docket                      today and overdue
docket myday                My Day (resets each day)     docket suggest   suggestions for it
docket important | planned | all | completed
docket upcoming | overdue | open | done
docket search invoice
docket add "Send invoice friday !3" -p work -l admin
docket show 12
docket done 12 14           docket undo 14   (repeating tasks move to their next date)
docket myday add 12         docket myday rm 12
docket star 12              docket unstar 12
docket step 12 add "Price it"     docket step 12 done 1     docket step 12 undo 1     docket step 12 rm 1
docket repeat 12 weekdays   (daily, weekly, monthly, yearly, "every 3 weeks", none)
docket remind 12 "tomorrow 9:00"   ("in 2 hours", "fri 5pm", 2026-11-20T09:00, none)
docket cat 12 Client Urgent        docket notes 12 "Use the new template"
docket edit 12 -d none -P 2
docket move 12 doing        docket comment 12 "Called, waiting for PO"
docket delete 12 [--yes]
docket sub 12 "Book the venue friday"   docket parent 15 12   docket parent 15 rm 12   docket tree work
docket convert 12          (old steps -> subtasks)
docket people              docket assign 12 pat     docket unassign 12 pat
docket share work          docket share work pat edit   (view, edit or admin)
docket overview            docket ai "plan the launch next week" [--yes]
docket projects             docket board work
docket list add Clients --group Studio
docket list rename Studio "Studio work"     docket list color Studio "#4F8BFF"
docket notify --once        docket notify --watch   (Windows reminder notifications)
docket status               docket login
```

`--json` on any listing for scripts ([format](api.md#cli)). Quick-add words: `today`, `tomorrow`, weekdays,
`in 3 days`, `20/11`, `!1`–`!5`.

`docket notify --watch` shows a Windows notification when a reminder falls due, with **Complete** and
**Snooze 10 min** buttons; clicking it opens the app. The first notification registers Docket with Windows'
notification system for your account.
