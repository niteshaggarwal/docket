---
name: docket
description: Manage the user's tasks and to-dos in Docket, a self-hosted Microsoft To Do style app on Vikunja, through the Docket connector's tools (list_tasks, my_day, add_task...) or the `docket` CLI. Use when the user asks what's on today, wants to plan their day, week or a project, or to add, find, change, complete or organise tasks, subtasks, My Day, important items, due dates, reminders, repeats, lists, boards, assignments or the overview, or mentions Docket or their to-do list.
---

# Docket

Docket keeps the user's tasks on their own Vikunja server and works like Microsoft To Do: **My Day** (resets each
morning), **Important** (starred), **Planned**, lists and groups, nested **subtasks**, steps, reminders, repeats and
Kanban boards. You reach it through the Docket connector (`docket-mcp`) or, on Windows, the `docket` command.

## 1. Find the tools

- **Connector:** look for tools named `list_tasks`, `my_day`, `add_task` and so on. The client may prefix them
  (`docket:list_tasks`, `mcp__docket__list_tasks`). Use them whenever they're available.
- **CLI:** no connector, but you can run commands on the user's Windows PC? Use `docket` (section 6).
- **Neither:** tell the user Docket isn't connected and point them to
  https://github.com/niteshaggarwal/docket/tree/main/docs/ai (Claude Desktop and Claude Code connect locally;
  claude.ai needs a Docket server and a custom connector). Don't pretend to have changed anything.

All 26 tools and their arguments are in [reference/tools.md](reference/tools.md).

## 2. Ground rules

- **Ids.** Tasks are written `#123`. Only use ids that came back from a tool in this conversation. Never guess or
  reuse an id from memory; look the task up first (`list_tasks` with `search`, or `get_task`). If a search finds
  several matches, ask which one.
- **Keep the user's words.** Use their wording for titles and notes. Don't rewrite, pad or "improve" them. Quick-add
  words in titles are fine: `today`, `tomorrow`, a weekday (`fri`), `in 3 days`, `20/11` (day/month) set the due date;
  `!1`–`!5` the priority. They're removed from the title.
- **Confirm before bulk changes.** Before completing, moving, rescheduling or re-categorising more than a handful of
  tasks, or anything the user didn't name one by one, show the list and ask. One clearly named change needs no
  confirmation.
- **No deleting, sharing or accounts.** The connector can't delete tasks or lists, share lists, manage teams or
  create people, and its token has no rights to. Say so plainly and tell the user where to do it in the Docket app:
  delete a task in its details pane (**Delete task**, with Undo); share with **List options > Share list…**; people
  and teams on the **People and teams** page. Offer what you *can* do instead (complete it, move it, rename it).
- **Replace vs add.** `set_notes`, `set_categories` and `set_reminder` replace what's there. To add to notes, read the
  task with `get_task` first and send old + new. Use `add_label` to add one category. `update_task`'s `description`
  replaces the steps too; prefer `set_notes`.
- **Dates and time zones.** "Today" and due dates follow the Docket server's time zone. Pass times the way the user
  said them (`tomorrow 9:00`, `fri 5pm`) or as local ISO without an offset (`2026-11-20T09:00`); don't convert to UTC.
  Only set a due date when the user gave or clearly implied one. If times come back hours off, the server's time zone
  (`TZ`) is probably wrong; tell the user.
- **Priority:** 0 none, 1 low, 2 medium, 3 high, 4 urgent, 5 do now. Set it only when urgency is stated.
- **Report briefly.** After changes, say what changed in a sentence or a short list, with the ids
  ("Added #57 *Send invoice*, due Fri, to Work, and put it in My Day").

## 3. Common requests

| The user asks | Do this |
|---|---|
| "What's on today?" / "What's on my plate?" | `my_day`, plus `list_tasks` with `view: "today"` (due today and overdue). Summarise: overdue first, then today, then My Day |
| "Plan my day" | `my_day` with `suggestions: true` and `list_tasks` `view: "today"`. Propose 3–6 tasks with a reason each, ask, then `set_my_day` with their ids (and `set_important` for the top one or two if they agree) |
| "What's coming up?" | `list_tasks` with `view: "upcoming"` (next 7 days) or `"planned"` |
| "How am I doing?" | `overview` (open, due today, overdue, next 7 days, done this week, by priority and list) |
| Add a task | `add_task` with `title` (quick-add words allowed), `project`, `labels`. Then, if asked: `set_my_day`, `set_important`, `set_reminder`, `set_repeat` on the returned id |
| "Remind me…" | `set_reminder` with `times`, e.g. `["thu 16:00"]`; `[]` clears |
| "Make it repeat…" | `set_repeat`: `daily`, `weekdays`, `weekly`, `monthly`, `yearly`, `every 3 days`, `none` |
| Find a task | `list_tasks` with `search` (add `view: "all"` or `"completed"` to widen) |
| Details of one task | `get_task` (steps, notes, reminders, repeat, categories, files, board column, comments) |
| Done / undo | `complete_task` with `ids`; `done: false` reopens. Repeating tasks move to their next date and stay open |
| Change title, due, priority, list | `update_task` (only the fields you pass change) |
| Notes and comments | `set_notes` (read first if adding); `add_comment` for progress notes |
| Categories | `add_label` (one) or `set_categories` (replace all) |
| Lists | `list_projects` (with groups, open counts and board columns); `create_list` with `title` and optional `group` |
| Boards | `get_board` for a list; `move_task` with `column` (moving to Done completes it) |
| Break work down | See section 4 |
| Assign to someone | `people` to find the username, then `assign`. The person must already see the list; if `assign` says they can't, ask the user to share the list in the app first |

## 4. Subtasks, steps and plans

- **Subtasks** are full tasks (own star, dates, reminders, subtasks) and nest to any depth. `add_subtask` with
  `parent_id` and `text` (quick-add words work) returns the new id; call it again on that id to go deeper.
  `link_subtask` puts an existing task under another one too (a task can have several parents; loops are refused).
  `task_tree` shows a list nested.
- **Steps** are a simple checklist inside one task (`steps` with `action` add, complete, uncomplete or remove; `index`
  is 1-based as shown by `get_task`). Prefer subtasks for real pieces of work; use steps only when the user wants a
  quick checklist.

To plan a project or "break #42 down":

1. Check what exists: `get_task` for #42, or `list_projects` and `list_tasks` with `search` so you don't duplicate.
2. Draft the plan in chat as a short nested list, with dates and priorities only where the user gave them. Ask
   "Shall I add this?"
3. On yes: create the parent with `add_task` (or use #42), then `add_subtask` for each child, then grandchildren on
   the returned ids. Keep titles short and in the user's words.
4. Finish with the tree (`task_tree`, or a summary with ids) and offer to put the first step in My Day.

## 5. Habits that help

- Look before you change: read the task or list first when anything is ambiguous.
- Prefer one clear question over a guess when a request could mean several tasks or lists.
- Keep results short. Show tasks as `#id Title — due, list`, not raw tool output, unless the user wants detail.
- Don't create lists the user didn't ask for; new tasks without a list go to their default list (usually Inbox).

## 6. Without the connector: the `docket` CLI (Windows)

If you can run commands on the user's PC and `docket` is installed, the same rules apply. Add `--json` to listings to
read them reliably. Ask before running anything that changes many tasks.

| Need | Command |
|---|---|
| Today and overdue / My Day / suggestions | `docket` · `docket myday` · `docket suggest` |
| Other views | `docket important`, `planned`, `all`, `completed`, `upcoming`, `overdue`, `open`, `done` |
| Search / one task | `docket search invoice` · `docket show 12` |
| Add | `docket add "Send invoice friday !3" -p Work -l Admin` |
| Complete / reopen | `docket done 12 14` · `docket undo 14` |
| My Day / star | `docket myday add 12` · `docket myday rm 12` · `docket star 12` · `docket unstar 12` |
| Edit | `docket edit 12 -t "New title" -d fri -P 3 -p Home` (`-d none` clears the date) |
| Reminders / repeat | `docket remind 12 "tomorrow 9:00"` · `docket repeat 12 weekdays` |
| Notes / comment / categories | `docket notes 12 "text"` · `docket comment 12 "text"` · `docket cat 12 Client Urgent` |
| Subtasks | `docket sub 12 "Book venue fri"` · `docket parent 15 12` · `docket tree Work` |
| Boards | `docket board Work` · `docket move 12 doing` |
| People | `docket people` · `docket assign 12 pat` |
| Lists / overview | `docket projects` · `docket list add Clients --group Studio` · `docket overview` |

The CLI can also delete (`docket delete 12`) and share (`docket share Work pat edit`) because it uses the user's own
token. Only do either when the user explicitly asks, and confirm first.

## 7. Example prompts

- "What's on my plate today? Put the three most urgent in My Day."
- "Add 'Send the Q3 invoice' to Work, due Friday, high priority, and remind me Thursday at 4pm."
- "Break #42 into subtasks: research, draft, review, send."
- "Plan the product launch next week as a project with subtasks, then show me the tree."
- "Everything overdue in Home: move it to next Monday." (confirm the list first)
- "Make 'Water the plants' repeat every 3 days."
- "Move #18 to Doing on the Website board and assign it to pat."
- "How did this week go?"
