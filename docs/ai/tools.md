# Tool reference

These are the tools the Docket connector (`docket-mcp`) gives an AI assistant. The same tools are available over
MCP, and as REST at `POST /api/tools/{name}` ([developer API](../api.md#rest-tools-and-openapi)).

A few things hold for all of them:

- **Nothing deletes or shares.** Deleting tasks or lists, sharing lists and managing teams are left to the Docket
  apps, and the connector's token couldn't do them anyway.
- **Results are readable text**, with task ids written as `#123`. Use those ids in follow-up calls.
- **Dates** accept `2026-11-20`, `2026-11-20T15:00`, `today`, `tomorrow`, a weekday (`fri`), `in 3 days`, `20/11`
  (day/month) or `none` to clear.
- **Lists** (Vikunja calls them projects) can be named or given by id.
- **Read-only tools** are marked as such to MCP clients (`readOnlyHint`), so Claude and ChatGPT can run them without
  asking. Changing tools ask first, unless you allow them.

<!-- sync:tools -->
Docket's connector has **26 tools**: 8 only read, and 18 change tasks. None delete or share.

| Tool | Changes data | In short |
|---|---|---|
| [`list_tasks`](#list_tasks) | no | List the user's tasks. |
| [`get_task`](#get_task) | no | Full details of one task: important, My Day, due date, repeat, reminders, priority, categories, steps (numbered), notes, file attachments, its Kanban column and the board's columns, and comments. |
| [`add_task`](#add_task) | yes | Add a task. |
| [`update_task`](#update_task) | yes | Change a task's title, due date, priority or list. |
| [`complete_task`](#complete_task) | yes | Mark one or more tasks done, or reopen them with done=false. |
| [`move_task`](#move_task) | yes | Move a task to a column of its list's Kanban board, by column name (e.g. |
| [`add_comment`](#add_comment) | yes | Add a comment to a task, for progress notes or context. |
| [`add_label`](#add_label) | yes | Attach one category (label) to a task, keeping its others. |
| [`list_projects`](#list_projects) | no | All lists (projects, with groups shown as Parent / Child) with their open-task counts and Kanban columns. |
| [`get_board`](#get_board) | no | A list's Kanban board: each column with its tasks. |
| [`create_project`](#create_project) | yes | Create a list (a project; it gets a list view and a Kanban board with To-Do, Doing and Done). |
| [`my_day`](#my_day) | no | The user's My Day list (resets each day), with its completed tasks. |
| [`set_my_day`](#set_my_day) | yes | Add tasks to My Day (in_my_day=true, default) or remove them (false). |
| [`set_important`](#set_important) | yes | Mark tasks important (starred) or not. |
| [`steps`](#steps) | yes | Work with a task's steps (a checklist inside the task). |
| [`set_repeat`](#set_repeat) | yes | Make a task repeat, or stop it repeating. |
| [`set_reminder`](#set_reminder) | yes | Replace a task's reminders. |
| [`set_categories`](#set_categories) | yes | Replace a task's categories (labels) with these names; missing ones are created. |
| [`set_notes`](#set_notes) | yes | Replace a task's notes (plain text). |
| [`create_list`](#create_list) | yes | Create a list, optionally inside a group (an existing list used as its parent). |
| [`add_subtask`](#add_subtask) | yes | Add a subtask under a task. |
| [`link_subtask`](#link_subtask) | yes | Make an existing task a subtask of another one too. |
| [`task_tree`](#task_tree) | no | A list's open tasks as a tree, with subtasks indented under their parents (a task with two parents appears under both). |
| [`assign`](#assign) | yes | Assign a task to a person who can already see its list (see the people tool). |
| [`people`](#people) | no | Everyone the user works with on their lists (usernames and names). |
| [`overview`](#overview) | no | Dashboard numbers: open, due today, overdue, next 7 days, done this week, by priority and per list. |

## Tasks, lists and boards

### list_tasks

List the user's tasks. Views: today (due today or overdue), upcoming (next 7 days), overdue, open (everything unfinished), done (recently finished), my_day (the user's My Day list), important (starred), planned (has a due date, grouped Earlier/Today/Tomorrow/This week/Later), all (every open task), completed. Optionally narrow to one list (project) or search words. Lines show ★ for important, My Day, steps progress, ↻ repeat and ⏰ next reminder.

*Read-only.*

- `view` (text): today | upcoming | overdue | open | done | my_day | important | planned | all | completed. Default today.
- `project` (text): List (project) name or id to narrow to. Optional.
- `search` (text): Words to search in titles and notes. Optional.
- `limit` (integer): Maximum tasks to return, 1-200. Default 50.

### get_task

Full details of one task: important, My Day, due date, repeat, reminders, priority, categories, steps (numbered), notes, file attachments, its Kanban column and the board's columns, and comments.

*Read-only.*

- **`id`** (integer, required): Task id, e.g. 12

### add_task

Add a task. The title may include quick-add words, which are taken out of the title: today, tomorrow, a weekday, "in 3 days", 20/11 for the due date and !1 to !5 for priority ("Send invoice friday !3"). Without a list it goes to the user's default list (usually Inbox). Use set_my_day, set_important, steps, set_repeat and set_reminder afterwards for the rest.

*Changes data.*

- **`title`** (text, required): Task title, optionally with quick-add words.
- `project` (text): List (project) name or id. Optional.
- `due` (text): Due date: 2026-11-20, 2026-11-20T15:00, today, tomorrow, a weekday (fri), "in 3 days", 20/11 (day/month), or none to clear. Optional; overrides quick-add words.
- `priority` (integer): 0 none, 1 low, 2 medium, 3 high, 4 urgent, 5 do now. Optional.
- `description` (text): Notes for the task. Optional.
- `labels` (list of text): Category (label) names to attach; missing ones are created. Optional.

### update_task

Change a task's title, due date, priority or list. Only the fields you pass change. To change notes without losing steps use set_notes; `description` here replaces the whole description including steps.

*Changes data.*

- **`id`** (integer, required): Task id.
- `title` (text): New title. Optional.
- `description` (text): Replaces the entire description, including steps. Prefer set_notes. Optional.
- `due` (text): Due date: 2026-11-20, 2026-11-20T15:00, today, tomorrow, a weekday (fri), "in 3 days", 20/11 (day/month), or none to clear. Optional.
- `priority` (integer): 0-5. Optional.
- `project` (text): Move to this list (name or id). Optional.

### complete_task

Mark one or more tasks done, or reopen them with done=false. A repeating task moves to its next date and stays open, with its steps unticked.

*Changes data.*

- **`ids`** (list of ids, required): Task ids.
- `done` (true/false): true marks done (default), false reopens.

### move_task

Move a task to a column of its list's Kanban board, by column name (e.g. Doing). Moving into the Done column marks it done.

*Changes data.*

- **`id`** (integer, required): Task id.
- **`column`** (text, required): Column name, e.g. To-Do, Doing, Done.

### add_comment

Add a comment to a task, for progress notes or context.

*Changes data.*

- **`id`** (integer, required): Task id.
- **`text`** (text, required): Comment text.

### add_label

Attach one category (label) to a task, keeping its others. The category is created if it doesn't exist. To replace all categories use set_categories.

*Changes data.*

- **`id`** (integer, required): Task id.
- **`label`** (text, required): Category name.

### list_projects

All lists (projects, with groups shown as Parent / Child) with their open-task counts and Kanban columns.

*Read-only.*

No arguments.

### get_board

A list's Kanban board: each column with its tasks.

*Read-only.*

- **`project`** (text, required): List (project) name or id.

### create_project

Create a list (a project; it gets a list view and a Kanban board with To-Do, Doing and Done). Optionally inside a parent list, which then acts as a group. Same as create_list.

*Changes data.*

- **`title`** (text, required): List name.
- `parent` (text): Parent list (group) name or id. Optional.

## My Day, stars, steps, repeats and reminders

### my_day

The user's My Day list (resets each day), with its completed tasks. With suggestions=true also lists suggested tasks to add (overdue, due today, upcoming, recently added).

*Read-only.*

- `suggestions` (true/false): Also show suggestions. Default false.

### set_my_day

Add tasks to My Day (in_my_day=true, default) or remove them (false).

*Changes data.*

- **`ids`** (list of ids, required): Task ids.
- `in_my_day` (true/false): true adds, false removes. Default: `true`.

### set_important

Mark tasks important (starred) or not.

*Changes data.*

- **`ids`** (list of ids, required): Task ids.
- `important` (true/false): true stars, false unstars. Default: `true`.

### steps

Work with a task's steps (a checklist inside the task). action: add (needs text), complete / uncomplete / remove (need index, 1-based as shown by get_task). Returns the updated steps.

*Changes data.*

- **`id`** (integer, required): Task id.
- **`action`** (text, required): add | complete | uncomplete | remove
- `text` (text): Step text, for add.
- `index` (integer): Step number (1-based), for complete, uncomplete and remove.

### set_repeat

Make a task repeat, or stop it repeating. When it's completed it moves to the next date and stays open. A task without a due date gets today as its due date.

*Changes data.*

- **`id`** (integer, required): Task id.
- **`repeat`** (text, required): daily | weekdays | weekly | monthly | yearly | "every N days|weeks|months|years" | none

### set_reminder

Replace a task's reminders. Each time: ISO date-time (2026-11-20T09:00), or natural: "tomorrow 9:00", "in 2 hours", "fri 5pm", "today 17:30", "20/11 14:00". An empty list clears all reminders. Reminders show as Windows notifications on the user's PC.

*Changes data.*

- **`id`** (integer, required): Task id.
- **`times`** (list of text, required): Reminder times; empty clears.

### set_categories

Replace a task's categories (labels) with these names; missing ones are created. An empty list clears them. My Day and repeat settings are kept.

*Changes data.*

- **`id`** (integer, required): Task id.
- **`categories`** (list of text, required): Category names.

### set_notes

Replace a task's notes (plain text). Its steps are kept. Read the task first if you mean to add to the existing notes.

*Changes data.*

- **`id`** (integer, required): Task id.
- **`notes`** (text, required): The new notes, plain text. Empty clears them.

### create_list

Create a list, optionally inside a group (an existing list used as its parent).

*Changes data.*

- **`title`** (text, required): List name.
- `group` (text): Group (parent list) name or id. Optional.

## Subtasks, people and the overview

### add_subtask

Add a subtask under a task. Subtasks are full tasks (own star, dates, reminders, subtasks) and can nest to any depth. Quick-add words work in the text: "Book train friday !3".

*Changes data.*

- **`parent_id`** (integer, required): Parent task id.
- **`text`** (text, required): Subtask text.

### link_subtask

Make an existing task a subtask of another one too. A task may have several parents. Refuses loops.

*Changes data.*

- **`parent_id`** (integer, required): Parent task id.
- **`child_id`** (integer, required): The task that goes under it.

### task_tree

A list's open tasks as a tree, with subtasks indented under their parents (a task with two parents appears under both).

*Read-only.*

- `list` (text): List name or id. Default: the default list.

### assign

Assign a task to a person who can already see its list (see the people tool). Use the username.

*Changes data.*

- **`id`** (integer, required): Task id.
- **`person`** (text, required): Username or display name.

### people

Everyone the user works with on their lists (usernames and names).

*Read-only.*

No arguments.

### overview

Dashboard numbers: open, due today, overdue, next 7 days, done this week, by priority and per list.

*Read-only.*

No arguments.

This reference is generated from `src/Docket.Mcp` by `tools/build-docs.mjs`, so it always matches the connector.
<!-- /sync:tools -->
