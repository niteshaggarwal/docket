# The Docket skill for Claude

The Docket skill teaches Claude how to work well with your tasks: which Docket tool to use for "what's on today" or
"plan my day", how to break work into nested subtasks, when to ask before changing many tasks, and what to tell you
when you ask for something the connector can't do (like deleting). It's free and open source, like Docket.

<p><a class="download" href="../../site/downloads/docket-skill.zip" download>Download docket-skill.zip</a></p>

The skill is instructions, not a connection. It **pairs with the Docket connector**: connect Docket first
([Claude](claude.md)), then add the skill. Without a connector, Claude can still use the skill with the `docket`
command line in Claude Code on Windows.

## What's in it

| File | What |
|---|---|
| `docket/SKILL.md` | When to use the skill, the ground rules, common requests mapped to tools, planning steps, CLI fallback, example prompts |
| `docket/reference/tools.md` | All 26 connector tools and their arguments, generated from Docket's source |

Read it on GitHub: [skills/docket/SKILL.md](../../skills/docket/SKILL.md).

The skill's description, which tells Claude when to use it:

> Manage the user's tasks and to-dos in Docket, a self-hosted Microsoft To Do style app on Vikunja, through the
> Docket connector's tools (list_tasks, my_day, add_task...) or the `docket` CLI. Use when the user asks what's on
> today, wants to plan their day, week or a project, or to add, find, change, complete or organise tasks, subtasks,
> My Day, important items, due dates, reminders, repeats, lists, boards, assignments or the overview, or mentions
> Docket or their to-do list.

## Install in claude.ai and the Claude apps

Skills you upload on claude.ai also work in the Claude desktop and mobile apps.

1. Make sure **code execution** is on: Settings > Capabilities > **Code execution and file creation** (on Team and
   Enterprise plans an owner turns on skills for the organization).
2. Download [docket-skill.zip](../../site/downloads/docket-skill.zip). Don't unzip it.
3. In Claude, open the **Skills** settings and choose to **upload a skill**, then pick the zip.
4. Turn the Docket skill on.

Claude's menus change from time to time; Anthropic's help article
[Use skills in Claude](https://support.claude.com/en/articles/12512180-use-skills-in-claude) has the current steps
and plan requirements.

## Install in Claude Code

Claude Code reads skills from folders. Unzip `docket-skill.zip` and copy the `docket` folder to either:

| For | Folder |
|---|---|
| Every project | `~/.claude/skills/docket/` (Windows: `%USERPROFILE%\.claude\skills\docket\`) |
| One project (shared with the repo) | `.claude/skills/docket/` in that project |

So that you end up with `~/.claude/skills/docket/SKILL.md`. From a clone of the Docket repository you can copy it
directly:

```sh
mkdir -p ~/.claude/skills && cp -r skills/docket ~/.claude/skills/
```

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.claude\skills" | Out-Null
Copy-Item -Recurse skills\docket "$env:USERPROFILE\.claude\skills\"
```

Start a new Claude Code session; the skill is used automatically when you talk about your tasks. Connect Docket too,
locally or remotely: [Claude Code setup](claude.md#claude-code).

## Try it

- "What's on my plate today? Put the three most urgent in My Day."
- "Plan the product launch next week as a project with subtasks, then show me the tree."
- "Break #42 into subtasks: research, draft, review, send."
- "Add 'Send the Q3 invoice' to Work, due Friday, high priority, and remind me Thursday at 4pm."
- "How did this week go?"

## Using it with other assistants

`SKILL.md` is plain Markdown. For ChatGPT, a custom GPT or a local model, paste its sections 2 to 5 into the
instructions or system prompt; they work the same with any assistant that has the Docket tools.

## Updating

When a new Docket release adds tools, download the new zip and upload it again (or copy the folder again). The tool
reference inside is regenerated with every release.
