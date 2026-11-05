namespace Docket.Core;

/// <summary>
/// A task in a nested list. Subtasks are real tasks (with their own star, dates, reminders and subtasks),
/// joined by Vikunja's subtask relation, so they can nest to any depth and one task can sit under several parents.
/// </summary>
public sealed record TaskNode(TaskInfo Info, int Depth, string Key, IReadOnlyList<TaskNode> Children)
{
    public long Id => Info.Id;
    public bool HasChildren => Children.Count > 0;
    public int DoneChildren => Children.Count(c => c.Info.Task.Done);
}

/// <summary>A row of a nested list once it is laid out, with its indent and whether it can expand.</summary>
public sealed record TaskRow(TaskNode Node, bool Expanded)
{
    public int Depth => Node.Depth;
}

/// <summary>Open tasks as a tree, completed tasks flat (each with its subtasks).</summary>
public sealed record TreeContent(IReadOnlyList<TaskNode> Open, IReadOnlyList<TaskNode> Completed);

public sealed partial class DocketService
{
    /// <summary>
    /// Builds the tree for a set of root candidates. A task is a root unless one of its parents is also in
    /// <paramref name="roots"/>. Children are looked up in <paramref name="known"/> (all tasks fetched), so a
    /// subtask kept in another list still shows under its parent. Loops are cut, so a cycle can't hang the app.
    /// </summary>
    public static List<TaskNode> BuildTree(IReadOnlyList<TaskInfo> roots, IReadOnlyDictionary<long, TaskInfo> known)
    {
        var rootIds = roots.Select(r => r.Id).ToHashSet();
        var top = roots.Where(r => !r.Task.Parents.Any(p => rootIds.Contains(p.Id)));
        return top.Select(r => Node(r, 0, r.Id.ToString(), [r.Id], known)).ToList();
    }

    private static TaskNode Node(TaskInfo info, int depth, string key, HashSet<long> path, IReadOnlyDictionary<long, TaskInfo> known)
    {
        var kids = new List<TaskNode>();
        foreach (var sub in info.Task.Subtasks.OrderBy(s => s.Done).ThenBy(s => s.Position).ThenBy(s => s.Id))
        {
            if (path.Contains(sub.Id)) continue;
            var child = known.TryGetValue(sub.Id, out var full) ? full : new TaskInfo(sub, info.Project);
            kids.Add(Node(child, depth + 1, $"{key}/{sub.Id}", [.. path, sub.Id], known));
        }
        return new TaskNode(info, depth, key, kids);
    }

    /// <summary>Lays a tree out as rows: a row's children follow it when its key is in <paramref name="expanded"/>.</summary>
    public static List<TaskRow> Flatten(IEnumerable<TaskNode> nodes, ISet<string> expanded)
    {
        var rows = new List<TaskRow>();
        void Walk(TaskNode n)
        {
            var open = expanded.Contains(n.Key);
            rows.Add(new TaskRow(n, open));
            if (open) foreach (var c in n.Children) Walk(c);
        }
        foreach (var n in nodes) Walk(n);
        return rows;
    }

    /// <summary>A list as a tree: top-level tasks with their subtasks nested under them.</summary>
    public async Task<TreeContent> ProjectTreeAsync(long projectId, SortMode sort, CancellationToken ct = default)
    {
        var open = await client.GetTasksAsync("done = false", ct: ct);
        var done = await client.GetTasksAsync("done = true", "done_at", "desc", ct);
        var known = new Dictionary<long, TaskInfo>();
        foreach (var t in open.Concat(done)) known[t.Id] = new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct));
        var openRoots = Sort(open.Where(t => t.ProjectId == projectId), sort).Select(t => known[t.Id]).ToList();
        var doneRoots = done.Where(t => t.ProjectId == projectId).Take(200).Select(t => known[t.Id]).ToList();
        // A done subtask of an open parent stays under its parent, not in Completed.
        var openIds = openRoots.Select(r => r.Id).ToHashSet();
        doneRoots = doneRoots.Where(d => !d.Task.Parents.Any(p => openIds.Contains(p.Id))).ToList();
        return new TreeContent(BuildTree(openRoots, known), BuildTree(doneRoots, known));
    }

    /// <summary>Wraps a flat list (a smart list) as one-level nodes that still carry their subtasks.</summary>
    public async Task<TreeContent> AsTreeAsync(ListContent content, CancellationToken ct = default)
    {
        var known = content.Open.Concat(content.Completed).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var t in await client.GetTasksAsync("done = false", ct: ct))
            known.TryAdd(t.Id, new TaskInfo(t, await ProjectNameAsync(t.ProjectId, ct)));
        List<TaskNode> Wrap(IEnumerable<TaskInfo> list) => list.Select(i => Node(i, 0, i.Id.ToString(), [i.Id], known)).ToList();
        return new TreeContent(Wrap(content.Open), Wrap(content.Completed));
    }

    // ---------- subtasks ----------
    /// <summary>Creates a subtask in the parent's list. Quick-add words (dates, !priority) work in the title.</summary>
    public async Task<TaskInfo> AddSubtaskAsync(long parentId, string text, CancellationToken ct = default)
    {
        var parent = await GetAsync(parentId, ct);
        var q = QuickAdd.Parse(text, Now);
        if (q.Title.Length == 0) throw new DocketException("The subtask is empty.");
        var child = await client.CreateTaskAsync(parent.ProjectId, q.Title, q.Due, q.Priority, ct) ?? throw new DocketException("Vikunja didn't return the new subtask.");
        await client.AddRelationAsync(parentId, child.Id, "subtask", ct);
        return new TaskInfo(await GetAsync(child.Id, ct), await ProjectNameAsync(child.ProjectId, ct));
    }

    /// <summary>Puts a task under another one too (a task can have several parents).</summary>
    public async Task LinkSubtaskAsync(long parentId, long childId, CancellationToken ct = default)
    {
        if (parentId == childId) throw new DocketException("A task can't be its own subtask.");
        if (await IsDescendantAsync(childId, parentId, ct)) throw new DocketException("That would put the task inside its own subtask.");
        await client.AddRelationAsync(parentId, childId, "subtask", ct);
    }

    /// <summary>Takes a task out from under one parent. Vikunja removes the reverse link itself.</summary>
    public Task UnlinkSubtaskAsync(long parentId, long childId, CancellationToken ct = default) =>
        client.RemoveRelationAsync(parentId, childId, "subtask", ct);

    private async Task<bool> IsDescendantAsync(long ancestorId, long candidateId, CancellationToken ct)
    {
        var seen = new HashSet<long>();
        var queue = new Queue<long>([ancestorId]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id)) continue;
            var t = await client.GetTaskAsync(id, ct);
            foreach (var s in t?.Subtasks ?? [])
            {
                if (s.Id == candidateId) return true;
                queue.Enqueue(s.Id);
            }
        }
        return false;
    }

    /// <summary>Turns a task's old checklist steps into real subtasks, keeping their order and ticks.</summary>
    public async Task<int> ConvertStepsToSubtasksAsync(long id, CancellationToken ct = default)
    {
        var t = await GetAsync(id, ct);
        var notes = TaskNotes.Parse(t.Description);
        if (notes.Steps.Count == 0) return 0;
        foreach (var step in notes.Steps)
        {
            var child = await client.CreateTaskAsync(t.ProjectId, step.Title, null, 0, ct) ?? throw new DocketException("Vikunja didn't return the new subtask.");
            await client.AddRelationAsync(id, child.Id, "subtask", ct);
            if (step.Done) await client.SetDoneAsync(child.Id, true, ct);
        }
        await SetNotesAndStepsAsync(id, n => n with { Steps = [] }, ct);
        return notes.Steps.Count;
    }
}
