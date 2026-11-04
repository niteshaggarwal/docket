# Using the Docket apps

## Windows app

Start menu > **Docket** (installed by `install.ps1`).

- **Smart lists:** My Day (resets each day; **Suggestions** offers overdue, due and recent tasks), Important, Planned
  (grouped Earlier, Today, Tomorrow, This week, Later), All, Completed, and Tasks. Hide any of them in Settings.
- **Lists and groups** (Vikunja parent projects): new, rename, colour, move into a group, delete. Drag a task onto a
  list to move it; drag to reorder in "My order".
- **Task details:** steps, My Day, reminder, due date, repeat (daily, weekdays, weekly, monthly, yearly, custom),
  categories (coloured labels), files, list, notes, and delete with Undo.
- **Views:** sort by my order, importance, due date, A–Z or creation date; show or hide completed; copy a list as
  text; search; a Board view per list; light, dark or system theme.
- **Shortcuts:** Ctrl+N add · Ctrl+D complete · Ctrl+T My Day · Ctrl+I important · Delete · Enter details ·
  Esc close · Ctrl+F search · Ctrl+L new list · Ctrl+R or F5 refresh.
- **Reminders** pop up through `docket notify --watch` (the installer's `-Reminders` option starts it at sign-in).
- Weekdays, yearly and every-N-months/years repeats are handled by Docket (a "↻" label), so they advance when
  completed in Docket, the CLI, Android or an AI, but not in Vikunja's own web app.

## Android app

Sign in with your server's address (for a Windows install, use Tailscale; see
[phone access](self-hosting.md#reach-it-from-your-phone)). Docket makes a token for the phone; your password isn't stored.
Swipe right to add to My Day, left to delete. Reminders become Android notifications. Build notes: [ANDROID.md](../ANDROID.md).

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
