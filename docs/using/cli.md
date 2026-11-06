# The command line

The `docket` command shows and changes your tasks from a terminal on Windows. It's installed by the
[Windows installer](../setup/windows.md) and added to your PATH; open a **new** terminal after installing.

```powershell
docket                              # today and overdue
docket add "Send invoice friday !3" -p Work
docket done 12
```

Task ids are the numbers shown as `#12`. Lists can be given by name or id.

## Everyday commands

### See tasks

```powershell
docket                      # due today and overdue (same as: docket today)
docket myday                # My Day
docket suggest              # suggestions for My Day
docket important            # starred
docket planned              # with a due date, grouped Earlier, Today, Tomorrow, This week, Later
docket all                  # every open task
docket completed            # finished tasks
docket upcoming             # next 7 days
docket overdue
docket open                 # everything unfinished
docket done                 # recently finished
docket search invoice       # search titles and notes (--all includes finished tasks)
docket show 12              # one task in full
```

Options for listings: `-p <list>` narrows to one list, `-n <number>` limits how many, `--json` prints JSON.

### Add and change tasks

```powershell
docket add "Send invoice friday !3" -p work -l admin   # quick-add words, list, category
docket add "Plan Q1" -d 2026-12-01 -P 2 --desc "Draft first"
docket done 12 14                                       # complete (repeating tasks move to their next date)
docket undo 14                                          # reopen
docket edit 12 -t "New title" -d none -P 2              # title, due (none clears), priority 0-5
docket notes 12 "Use the new template"                  # replace notes (steps are kept)
docket comment 12 "Called, waiting for PO"
docket move 12 doing                                    # board column
docket delete 12 --yes                                  # without --yes it asks first
```

### My Day, stars, steps, repeats and reminders

```powershell
docket myday add 12 15      docket myday rm 12
docket star 12              docket unstar 12
docket step 12 add "Price it"     docket step 12 done 1     docket step 12 undo 1     docket step 12 rm 1
docket repeat 12 weekdays   # daily, weekdays, weekly, monthly, yearly, "every 3 weeks", none
docket remind 12 "tomorrow 9:00"   # "in 2 hours", "fri 5pm", 2026-11-20T09:00, none
docket cat 12 Client Urgent        # set categories (none clears)
docket label 12 Admin              # add one category
```

### Subtasks

```powershell
docket sub 12 "Book the venue friday"   # add a subtask under #12
docket parent 15 12                     # also put #15 under #12 (a task can have several parents)
docket parent 15 rm 12                  # take it out again
docket tree work                        # a list with subtasks nested
docket convert 12                       # turn old checklist steps into subtasks
```

### People and sharing

```powershell
docket people
docket assign 12 pat        docket unassign 12 pat
docket share work                     # who can see it
docket share work pat edit            # share with a username or email: view, edit or admin
```

### Overview and AI

```powershell
docket overview                                # dashboard numbers
docket ai "plan the launch next week"          # show a plan from the AI chosen in Settings > AI
docket ai "plan the launch next week" --yes    # show it and add it
```

### Lists and boards

```powershell
docket projects                              # all lists with counts
docket board work                            # a list's board
docket list add Clients --group Studio
docket list rename Studio "Studio work"
docket list color Studio "#4F8BFF"
```

### Reminders and setup

```powershell
docket notify --watch       # show reminder notifications all day (Ctrl+C stops)
docket notify --once        # show what's due now, then exit
docket status               # which server, its Vikunja version and how many lists
docket login --server http://localhost:3456   # connect with an API token
```

`docket notify --watch` shows a Windows notification when a reminder falls due, with **Complete** and **Snooze
10 min** buttons; clicking it opens the app. The installer's `-Reminders` option starts it at sign-in. If the task
server on your PC stops, it starts it again and keeps watching.

## Quick-add words

`today`, `tomorrow`, weekdays (`fri`), `in 3 days`, `20/11` (day/month) set the due date, and `!1` to `!5` the
priority. See [quick add](quick-add.md).

## JSON for scripts

Add `--json` to any listing:

```powershell
docket today --json | ConvertFrom-Json | Where-Object priority -ge 3
docket planned --json        # [{ group: "Today", task: {...} }, ...]
docket show 42 --json
```

The task format is in the [developer API](../api.md#cli). Exit codes: 0 success, 1 error, 2 wrong usage.

## Full help

This is the output of `docket --help`, kept in sync with the program:

<!-- sync:cli -->
```text
docket: your Vikunja tasks from the terminal

Lists
  docket [today]                 due today and overdue
  docket myday                   My Day (resets each day)
  docket suggest                 suggestions for My Day
  docket important | planned | all | completed
  docket upcoming | overdue | open | done
  docket search <words> [--all]  search titles and notes (--all includes done)
    options: -p <project>  -n <limit>  --json

Tasks
  docket add <text> [-p list] [-d due] [-P 0-5] [--desc text] [-l label]...
         quick-add words work in <text>: "Call Pat friday !3"
  docket show <id>
  docket done <id> [<id>...]     docket undo <id>        (repeating tasks move to their next date)
  docket myday add|rm <id>...    docket star|unstar <id>...
  docket step <id> add <text>    docket step <id> done|undo|rm <n>
  docket repeat <id> daily|weekdays|weekly|monthly|yearly|"every 3 weeks"|none
  docket remind <id> <when>|none    when: "tomorrow 9:00", "in 2 hours", "fri 5pm", 2026-11-20T09:00
  docket cat <id> <category>...  set categories (none clears)
  docket notes <id> <text>       replace notes (steps are kept)
  docket edit <id> [-t title] [-d due|none] [-P 0-5] [--desc text] [-p list]
  docket move <id> <column>      e.g. docket move 12 doing
  docket comment <id> <text>     docket label <id> <label>
  docket delete <id> [--yes]

Subtasks (real tasks, any depth, a task can have several parents)
  docket sub <parent id> <text>  add a subtask        docket tree [list]   show a list nested
  docket parent <id> <parent id> also put it under another task
  docket parent <id> rm <parent id>                   docket convert <id>  old steps -> subtasks

People and sharing
  docket people                  docket assign|unassign <id> <username>
  docket share <list>            who can see it
  docket share <list> <username|email> [view|edit|admin]

Overview and AI
  docket overview                dashboard numbers
  docket ai <what you need> [--yes]   plan tasks with the AI set in the Docket app's Settings

Lists and boards
  docket projects                docket board <list>
  docket list add <name> [--group <list>]
  docket list rename <list> <new name>
  docket list color <list> <#hex>

Reminders
  docket notify --once           show reminders that are due now
  docket notify --watch          keep showing them (Ctrl+C to stop)

Setup
  docket login [--server http://localhost:3456]   asks for an API token
  docket status
```
<!-- /sync:cli -->
