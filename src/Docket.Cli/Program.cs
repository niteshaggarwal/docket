using System.Text;
using System.Text.Json;
using Docket.Cli;
using Docket.Core;
using Docket.Shared;
using CommunityToolkit.WinUI.Notifications;

Console.OutputEncoding = Encoding.UTF8;

// Started by Windows because a reminder notification was clicked while no watcher was running.
if (ToastNotificationManagerCompat.WasCurrentProcessToastActivated())
{
    var done = new TaskCompletionSource();
    ToastNotificationManagerCompat.OnActivated += e => Notifier.HandleActivationAsync(e.Argument).ContinueWith(_ => done.TrySetResult());
    await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(15)));
    return 0;
}

try
{
    try { return await Cli.RunAsync(args); }
    catch (HttpRequestException ex) when (LocalServer.IsConnectionRefused(ex) && AppSettings.Load() is { } s && LocalServer.IsLocal(s.ServerUrl))
    {
        // The server on this PC has stopped: start it, then run the command again (it never reached the server).
        Console.Error.WriteLine("Starting your task server…");
        if (!await LocalServer.EnsureRunningAsync(s.ServerUrl, s.VikunjaExe)) throw new DocketException(LocalServer.StillDownMessage(s.ServerUrl));
        return await Cli.RunAsync(args);
    }
}
catch (UsageException ex) { Console.Error.WriteLine(ex.Message); Console.Error.WriteLine("Run \"docket help\" for the commands."); return 2; }
catch (DocketException ex) { Console.Error.WriteLine(ex.Message); return 1; }
catch (VikunjaException ex) { Console.Error.WriteLine(ex.Message); return 1; }
catch (HttpRequestException ex) { Console.Error.WriteLine($"Can't reach your Docket server: {ex.Message}"); return 3; }
catch (TaskCanceledException) { Console.Error.WriteLine("Vikunja didn't answer in time."); return 3; }

static class Cli
{
    private const string Help = """
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
        """;

    public static async Task<int> RunAsync(string[] argv)
    {
        var a = new Args(argv);
        var cmd = a.Words.Count > 0 ? a.Words[0].ToLowerInvariant() : "today";
        if (a.Has("--help") || a.Has("-h") || cmd is "help" or "?") { Console.WriteLine(Help); return 0; }

        var settings = AppSettings.Load();
        if (cmd == "login") return Login(a, settings);
        if (cmd == "notify")
        {
            if (!a.Has("--watch") && !a.Has("--once")) throw new UsageException("Use: docket notify --once  or  docket notify --watch");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            return await Notifier.RunAsync(a.Has("--watch"), cts.Token);
        }

        using var client = settings.CreateClient() ?? throw new DocketException("Not connected yet. Run: docket login");
        var svc = new DocketService(client) { State = settings };
        var now = DateTimeOffset.Now;
        var json = a.Has("--json");

        switch (cmd)
        {
            case "today" or "upcoming" or "week" or "overdue" or "open":
            case "done" when a.Words.Count == 1:
            {
                var view = DocketService.ParseView(cmd);
                var list = await svc.ListAsync(view, a.Opt("--project"), limit: a.IntOpt("--limit") ?? (view == TaskView.Done ? 20 : 100));
                return Print(list, json, now, view switch
                {
                    TaskView.Today => "Nothing due today.", TaskView.Upcoming => "Nothing due in the next 7 days.",
                    TaskView.Overdue => "Nothing overdue.", TaskView.Done => "Nothing finished yet.", _ => "No open tasks.",
                });
            }
            case "myday" or "my-day" or "md" when a.Words.Count == 1:
                return await Smart(svc, SmartList.MyDay, settings, json, now, "My Day is empty. Try: docket suggest", a);
            case "important" or "starred":
                return await Smart(svc, SmartList.Important, settings, json, now, "Nothing marked important.", a);
            case "all":
                return await Smart(svc, SmartList.All, settings, json, now, "No open tasks.", a);
            case "completed" or "finished":
                return await Smart(svc, SmartList.Completed, settings, json, now, "Nothing finished yet.", a);
            case "planned":
            {
                var c = await svc.SmartAsync(SmartList.Planned, SortMode.DueDate, settings.DefaultProjectId);
                if (json) return Json(c.Open.Select(t => new { group = DocketService.PlannedGroup(t.Task, now), task = Dto(t, now) }));
                Console.WriteLine(ToDoFormat.Planned(c.Open, now));
                return 0;
            }
            case "myday" or "my-day" or "md":
            {
                var sub = a.Words[1].ToLowerInvariant();
                if (sub is not ("add" or "rm" or "remove")) throw new UsageException("Use: docket myday add <id>...  or  docket myday rm <id>...");
                var ids = Ids(a, 2);
                foreach (var id in ids) One(await svc.SetMyDayAsync(id, sub == "add"), json, now, sub == "add" ? "Added to My Day" : "Removed from My Day");
                return 0;
            }
            case "suggest" or "suggestions":
            {
                var s = await svc.MyDaySuggestionsAsync();
                if (json) return Json(s.Select(x => new { x.Group, task = Dto(x.Task, now) }));
                Console.WriteLine(ToDoFormat.Suggestions(s, now));
                return 0;
            }
            case "star" or "unstar":
                foreach (var id in Ids(a, 1)) One(await svc.SetImportantAsync(id, cmd == "star"), json, now, cmd == "star" ? "Marked important" : "No longer important");
                return 0;
            case "step" or "steps":
                return await Step(svc, a, json, now);
            case "repeat":
            {
                var id = a.Id(1);
                var text = a.Rest(2);
                if (text.Length == 0) throw new UsageException("How should it repeat? daily, weekdays, weekly, monthly, yearly, \"every 3 weeks\" or none.");
                RepeatRule? rule = null;
                if (!text.Equals("none", StringComparison.OrdinalIgnoreCase) && !text.Equals("never", StringComparison.OrdinalIgnoreCase)
                    && !RepeatRule.TryParse(text, out rule!)) throw new UsageException($"Can't read \"{text}\". Use daily, weekdays, weekly, monthly, yearly or \"every 3 weeks\".");
                return One(await svc.SetRepeatAsync(id, rule), json, now, rule is null ? "No longer repeats" : $"Repeats {rule.Describe().ToLowerInvariant()}");
            }
            case "remind" or "reminder":
            {
                var id = a.Id(1);
                var text = a.Rest(2);
                if (text.Length == 0) throw new UsageException("When? e.g. docket remind 12 \"tomorrow 9:00\"  (or none)");
                var times = text.Equals("none", StringComparison.OrdinalIgnoreCase) ? [] : new[] { WhenParser.Parse(text, now) };
                var t = await svc.SetRemindersAsync(id, times);
                return One(t, json, now, times.Length == 0 ? "Reminders cleared" : $"Reminder set for {ToDoFormat.When(times[0], now)}");
            }
            case "cat" or "category" or "categories":
            {
                var id = a.Id(1);
                var names = a.Words.Skip(2).ToList();
                if (names.Count == 0) throw new UsageException("Which categories? e.g. docket cat 12 Client Urgent  (or none)");
                if (names.Count == 1 && names[0].Equals("none", StringComparison.OrdinalIgnoreCase)) names.Clear();
                return One(await svc.SetCategoriesAsync(id, names), json, now, "Categories set");
            }
            case "notes":
            {
                var id = a.Id(1);
                var text = a.Rest(2);
                return One(await svc.SetNotesAndStepsAsync(id, n => n with { Notes = text }), json, now, "Notes saved");
            }
            case "delete" or "rm" or "del":
            {
                var id = a.Id(1);
                var t = await svc.GetAsync(id);
                if (!a.Has("--yes") && !a.Has("-y"))
                {
                    if (Console.IsInputRedirected) throw new UsageException("Add --yes to delete without asking.");
                    Console.Write($"Delete #{id} \"{t.Title}\"? This can't be undone. [y/N] ");
                    if (!(Console.ReadLine() ?? "").Trim().StartsWith('y')) { Console.WriteLine("Kept."); return 0; }
                }
                await svc.DeleteTaskAsync(id);
                Console.WriteLine($"Deleted #{id} \"{t.Title}\".");
                return 0;
            }
            case "list" or "lists" when a.Words.Count >= 2:
                return await ListCommand(svc, a);
            case "search" or "find":
            {
                var q = a.Rest(1);
                if (q.Length == 0) throw new UsageException("What should I search for?");
                var list = await svc.ListAsync(a.Has("--all") ? TaskView.Done : TaskView.Open, a.Opt("--project"), q, a.IntOpt("--limit") ?? 50);
                return Print(list, json, now, $"No tasks match \"{q}\".");
            }
            case "sub" or "subtask":
            {
                var parent = a.Id(1);
                var text = a.Rest(2);
                if (text.Length == 0) throw new UsageException("Use: docket sub <parent id> <text>");
                return One(await svc.AddSubtaskAsync(parent, text), json, now, $"Added under #{parent}");
            }
            case "steps2sub" or "convert":
            {
                foreach (var id in Ids(a, 1)) Console.WriteLine($"#{id}: {await svc.ConvertStepsToSubtasksAsync(id)} step(s) turned into subtasks.");
                return 0;
            }
            case "parent":
            {
                var child = a.Id(1);
                var verb = a.Words.Count > 2 ? a.Words[2].ToLowerInvariant() : "";
                if (verb is "rm" or "remove") { await svc.UnlinkSubtaskAsync(a.Id(3, "parent id"), child); Console.WriteLine($"#{child} is no longer under #{a.Id(3)}."); return 0; }
                var parent = a.Id(2, "parent id");
                await svc.LinkSubtaskAsync(parent, child);
                Console.WriteLine($"#{child} is now also a subtask of #{parent}.");
                return 0;
            }
            case "tree":
            {
                var p = await svc.ResolveProjectAsync(a.Words.Count > 1 ? a.Rest(1) : settings.DefaultProjectId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var tree = await svc.ProjectTreeAsync(p.Id, SortMode.Custom);
                void Walk(TaskNode n) { Console.WriteLine($"{new string(' ', n.Depth * 2)}{(n.Info.Task.Done ? "[x]" : "[ ]")} #{n.Id} {n.Info.Task.Title}"); foreach (var c in n.Children) Walk(c); }
                Console.WriteLine(p.Title);
                foreach (var n in tree.Open) Walk(n);
                return 0;
            }
            case "assign" or "unassign":
            {
                var id = a.Id(1);
                var who = a.Rest(2);
                var people = await svc.PeopleAsync();
                var person = people.FirstOrDefault(u => u.Username.Equals(who, StringComparison.OrdinalIgnoreCase) || DocketService.DisplayName(u).Equals(who, StringComparison.OrdinalIgnoreCase))
                    ?? (cmd == "assign" ? (await client.SearchUsersAsync(who)).FirstOrDefault() : null)
                    ?? throw new DocketException($"No one called \"{who}\". Share a list with them first, or check the username.");
                var t = cmd == "assign" ? await svc.AssignAsync(id, person) : await svc.UnassignAsync(id, person.Id);
                return One(t, json, now, cmd == "assign" ? $"Assigned to {DocketService.DisplayName(person)}" : "Unassigned");
            }
            case "people":
            {
                var people = await svc.PeopleAsync();
                if (json) return Json(people.Select(u => new { u.Id, u.Username, name = DocketService.DisplayName(u) }));
                foreach (var u in people) Console.WriteLine($"{u.Username}{(string.IsNullOrEmpty(u.Name) ? "" : $" ({u.Name})")}");
                return 0;
            }
            case "share":
            {
                var p = await svc.ResolveProjectAsync(a.Words.Count > 1 ? a.Words[1] : throw new UsageException("Use: docket share <list> [<username|email> [view|edit|admin]]"));
                if (a.Words.Count > 2)
                {
                    var perm = (a.Words.Count > 3 ? a.Words[3].ToLowerInvariant() : "edit") switch
                    {
                        "view" or "read" => SharePermission.Read, "admin" => SharePermission.Admin, _ => SharePermission.ReadWrite,
                    };
                    await svc.ShareAsync(p.Id, a.Words[2], perm);
                    Console.WriteLine($"Shared \"{p.Title}\" with {a.Words[2]} ({Permissions.Describe(perm)}).");
                    return 0;
                }
                var s = await svc.SharingAsync(p.Id);
                Console.WriteLine($"\"{p.Title}\" is shared with:");
                foreach (var u in s.People) Console.WriteLine($"  {u.Username}: {Permissions.Describe(u.Permission)}");
                foreach (var t in s.Teams) Console.WriteLine($"  team {t.Name}: {Permissions.Describe(t.Permission)}");
                foreach (var l in s.Links) Console.WriteLine($"  link {DocketService.LinkShareUrl(settings.ServerUrl, l)}: {Permissions.Describe(l.Permission)}");
                if (s.People.Count + s.Teams.Count + s.Links.Count == 0) Console.WriteLine("  no one yet");
                return 0;
            }
            case "overview" or "dash" or "dashboard":
            {
                var o = await svc.OverviewAsync();
                Console.WriteLine($"Open {o.Open} · due today {o.DueToday} · overdue {o.Overdue} · next 7 days {o.Next7Days} · done this week {o.DoneThisWeek}");
                Console.WriteLine("Priority: " + string.Join("  ", o.ByPriority.Select((n, p) => $"{DocketService.PriorityName(p)} {n}")));
                foreach (var l in o.Lists) Console.WriteLine($"  {(l.Style.Icon is { } ic ? ic + " " : "")}{l.Project.Title}: {l.Open} open{(l.Overdue > 0 ? $", {l.Overdue} overdue" : "")}, {l.DoneThisWeek} done this week");
                return 0;
            }
            case "ai":
            {
                var request = a.Rest(1);
                if (request.Length == 0) throw new UsageException("Use: docket ai <what you need> [--yes]");
                var plans = await AiPlanner.PlanAsync(settings.Ai, request, now, (await svc.ProjectsAsync()).Select(p => p.Title));
                void Show(PlannedTask p, int depth) { Console.WriteLine($"{new string(' ', depth * 2)}- {p.Title}{(p.Due is { } d ? $" (due {d})" : "")}{(p.List is { } l ? $" [{l}]" : "")}"); foreach (var s in p.Subtasks ?? []) Show(s, depth + 1); }
                foreach (var p in plans) Show(p, 0);
                if (!a.Has("--yes")) { Console.WriteLine("Add these? Run again with --yes."); return 0; }
                var home = await svc.DefaultProjectAsync(settings.DefaultProjectId);
                foreach (var p in plans) await svc.AddPlannedAsync(p, home.Id);
                Console.WriteLine($"Added {plans.Count} task(s).");
                return 0;
            }
            case "add" or "a" or "new":
            {
                var text = a.Rest(1);
                var t = await svc.AddAsync(text, a.Opt("--project"), a.Opt("--due"), a.IntOpt("--priority"), a.Opt("--desc"), a.All("--label"), settings.DefaultProjectId);
                return One(t, json, now, "Added");
            }
            case "show" or "s":
            {
                var d = await svc.DetailAsync(a.Id(1));
                if (json) return Json(new
                {
                    task = Dto(d.Info, now),
                    notes = TaskNotes.Parse(d.Info.Task.Description).Notes,
                    files = d.Info.Task.Attachments?.Select(f => f.File.Name) ?? [],
                    comments = d.Comments.Select(c => new { c.Id, author = c.Author?.Username, c.Created, text = TaskFormat.StripHtml(c.Comment) }),
                    columns = d.Columns,
                });
                Console.WriteLine(ToDoFormat.Detail(d, now));
                return 0;
            }
            case "done" or "x" or "undo" or "reopen":
            {
                var done = cmd is "done" or "x";
                foreach (var id in Ids(a, 1))
                {
                    var before = done ? RepeatRule.From(await svc.GetAsync(id)) : null;
                    var t = await svc.SetDoneAsync(id, done);
                    One(t, json, now, !done ? "Reopened" : before is not null && !t.Task.Done ? "Done, next one" : "Done");
                }
                return 0;
            }
            case "edit":
            {
                var t = await svc.UpdateAsync(a.Id(1), a.Opt("--title"), a.Opt("--desc"), a.Opt("--due"), a.IntOpt("--priority"), a.Opt("--project"));
                return One(t, json, now, "Updated");
            }
            case "move" or "mv":
            {
                var column = a.Rest(2);
                if (column.Length == 0) throw new UsageException("Move it where? e.g. docket move 12 doing");
                return One(await svc.MoveAsync(a.Id(1), column), json, now, "Moved");
            }
            case "comment" or "note":
            {
                var text = a.Rest(2);
                await svc.CommentAsync(a.Id(1), text);
                Console.WriteLine($"Comment added to #{a.Id(1)}.");
                return 0;
            }
            case "label" or "tag":
            {
                var label = a.Rest(2);
                if (label.Length == 0) throw new UsageException("Which label?");
                var l = await svc.AddLabelAsync(a.Id(1), label);
                Console.WriteLine($"Labelled #{a.Id(1)} \"{l.Title}\".");
                return 0;
            }
            case "projects" or "ls" or "lists":
            {
                var ps = await svc.ProjectOverviewAsync();
                if (json) return Json(ps.Select(p => new { p.Project.Id, title = p.Project.Title, path = p.Path, open = p.OpenTasks, columns = p.Columns }));
                foreach (var p in ps) Console.WriteLine($"{p.Path} ({p.OpenTasks} open){(p.Columns.Count > 0 ? "  · " + string.Join(" › ", p.Columns) : "")}");
                return 0;
            }
            case "project":
            {
                if (a.Words.Count < 3 || !a.Words[1].Equals("add", StringComparison.OrdinalIgnoreCase)) throw new UsageException("Use: docket project add <name> [--parent <project>]");
                var p = await svc.CreateProjectAsync(a.Rest(2), a.Opt("--parent"));
                Console.WriteLine($"Created project \"{p.Title}\".");
                return 0;
            }
            case "board" or "b":
            {
                var name = a.Rest(1);
                if (name.Length == 0) throw new UsageException("Which project? e.g. docket board work");
                var p = await svc.ResolveProjectAsync(name);
                var cols = await svc.BoardAsync(name);
                if (json) return Json(cols.Select(c => new { c.Title, c.IsDone, tasks = c.Tasks.Select(t => Dto(t, now)) }));
                Console.WriteLine(TaskFormat.Board(p.Title, cols, now));
                return 0;
            }
            case "status":
            {
                var version = await client.GetServerVersionAsync();
                var ps = await svc.ProjectsAsync();
                Console.WriteLine($"Connected to {settings.ServerUrl} (Vikunja {version}), {ps.Count} projects.");
                Console.WriteLine($"Claude token: {(settings.ClaudeToken is null ? "not set" : "set")}. Settings: {AppSettings.FilePath}");
                return 0;
            }
            default:
                throw new UsageException($"Unknown command \"{cmd}\".");
        }
    }

    private static async Task<int> Smart(DocketService svc, SmartList list, AppSettings settings, bool json, DateTimeOffset now, string empty, Args a)
    {
        var key = "smart:" + list;
        var sort = settings.Sorts.TryGetValue(key, out var s) ? s : list == SmartList.MyDay ? SortMode.Custom : SortMode.DueDate;
        var c = await svc.SmartAsync(list, sort, settings.DefaultProjectId);
        if (json) return Json(new { open = c.Open.Select(t => Dto(t, now)), completed = c.Completed.Select(t => Dto(t, now)) });
        Console.WriteLine(ToDoFormat.Content(c, now, empty, a.IntOpt("--limit") ?? 10));
        return 0;
    }

    private static async Task<int> Step(DocketService svc, Args a, bool json, DateTimeOffset now)
    {
        var id = a.Id(1);
        if (a.Words.Count < 3)
        {
            var notes = TaskNotes.Parse((await svc.GetAsync(id)).Description);
            if (notes.Steps.Count == 0) { Console.WriteLine($"#{id} has no steps. Add one: docket step {id} add <text>"); return 0; }
            for (var i = 0; i < notes.Steps.Count; i++) Console.WriteLine($"{i + 1}. [{(notes.Steps[i].Done ? "x" : " ")}] {notes.Steps[i].Title}");
            return 0;
        }
        var sub = a.Words[2].ToLowerInvariant();
        TaskInfo t = sub switch
        {
            "add" => await svc.AddStepAsync(id, a.Rest(3)),
            "done" or "x" => await svc.SetStepDoneAsync(id, StepNo(a), true),
            "undo" => await svc.SetStepDoneAsync(id, StepNo(a), false),
            "rm" or "remove" or "delete" => await svc.RemoveStepAsync(id, StepNo(a)),
            _ => throw new UsageException("Use: docket step <id> add <text> | done <n> | undo <n> | rm <n>"),
        };
        return One(t, json, now, "Steps updated");
    }

    private static int StepNo(Args a)
    {
        if (a.Words.Count < 4 || !int.TryParse(a.Words[3], out var n) || n < 1) throw new UsageException("Which step? Give its number, e.g. docket step 12 done 2");
        return n - 1;
    }

    private static async Task<int> ListCommand(DocketService svc, Args a)
    {
        var sub = a.Words[1].ToLowerInvariant();
        switch (sub)
        {
            case "add" or "new":
            {
                var name = a.Rest(2);
                if (name.Length == 0) throw new UsageException("Name the list: docket list add <name> [--group <list>]");
                var p = await svc.CreateProjectAsync(name, a.Opt("--group") ?? a.Opt("--parent"));
                Console.WriteLine($"Created list \"{p.Title}\".");
                return 0;
            }
            case "rename":
            {
                if (a.Words.Count < 4) throw new UsageException("Use: docket list rename <list> <new name>");
                var p = await svc.ResolveProjectAsync(a.Words[2]);
                var r = await svc.RenameListAsync(p.Id, title: a.Rest(3));
                Console.WriteLine($"Renamed \"{p.Title}\" to \"{r.Title}\".");
                return 0;
            }
            case "color" or "colour":
            {
                if (a.Words.Count < 4) throw new UsageException("Use: docket list color <list> <#hex>");
                var hex = a.Words[^1].TrimStart('#');
                if (hex.Length != 6 || !hex.All(Uri.IsHexDigit)) throw new UsageException($"\"{a.Words[^1]}\" isn't a colour like #4F8BFF.");
                var p = await svc.ResolveProjectAsync(string.Join(' ', a.Words.Skip(2).Take(a.Words.Count - 3)));
                await svc.RenameListAsync(p.Id, hexColor: hex);
                Console.WriteLine($"\"{p.Title}\" is now #{hex.ToUpperInvariant()}.");
                return 0;
            }
            default:
                throw new UsageException("Use: docket list add|rename|color ...");
        }
    }

    private static List<long> Ids(Args a, int from)
    {
        if (a.Words.Count <= from) throw new UsageException("Which task? Give its id, e.g. 12");
        return Enumerable.Range(from, a.Words.Count - from).Select(i => a.Id(i)).ToList();
    }

    private static int Login(Args a, AppSettings settings)
    {
        var server = a.Opt("--server") ?? settings.ServerUrl;
        Console.Write("Vikunja API token (Settings > API Tokens): ");
        var token = ReadSecret();
        if (string.IsNullOrWhiteSpace(token)) throw new UsageException("No token entered.");
        using var probe = new VikunjaClient(new Uri(server), token.Trim());
        var version = probe.GetServerVersionAsync().GetAwaiter().GetResult();
        probe.GetProjectsAsync().GetAwaiter().GetResult();
        settings.ServerUrl = server.TrimEnd('/');
        settings.Token = token.Trim();
        settings.Save();
        Console.WriteLine($"Connected to Vikunja {version}. Token saved, encrypted for your Windows account.");
        return 0;
    }

    private static string ReadSecret()
    {
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); return sb.ToString(); }
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
    }

    private static object Dto(TaskInfo t, DateTimeOffset now)
    {
        var notes = TaskNotes.Parse(t.Task.Description);
        return new
        {
            id = t.Id, title = t.Task.Title, done = t.Task.Done, project = t.Project, projectId = t.Task.ProjectId,
            due = t.Task.Due, dueText = TaskFormat.Due(t.Task, now), priority = t.Task.Priority, column = t.Column,
            important = t.Task.IsFavorite, myDay = DocketService.InMyDay(t.Task),
            steps = notes.Steps.Select(s => new { s.Title, s.Done }), repeat = RepeatRule.From(t.Task)?.Describe(),
            nextReminder = t.Task.NextReminder(now),
            categories = (t.Task.Labels ?? []).Where(DocketService.IsCategory).Select(l => l.Title),
        };
    }

    private static int Print(List<TaskInfo> list, bool json, DateTimeOffset now, string empty)
    {
        if (json) return Json(list.Select(t => Dto(t, now)));
        Console.WriteLine(ToDoFormat.List(list, now, empty));
        return 0;
    }

    private static int One(TaskInfo t, bool json, DateTimeOffset now, string verb)
    {
        if (json) return Json(Dto(t, now));
        Console.WriteLine($"{verb}: {ToDoFormat.Line(t, now)}");
        return 0;
    }

    private static readonly JsonSerializerOptions JsonOut = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static int Json(object o) { Console.WriteLine(JsonSerializer.Serialize(o, JsonOut)); return 0; }
}
