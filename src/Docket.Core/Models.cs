using System.Text.Json.Serialization;

namespace Docket.Core;

/// <summary>A Vikunja project. Only the fields Docket uses are mapped.</summary>
public sealed class VProject
{
    [JsonPropertyName("identifier")] public string Identifier { get; set; } = "";
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("hex_color")] public string HexColor { get; set; } = "";
    [JsonPropertyName("parent_project_id")] public long ParentProjectId { get; set; }
    [JsonPropertyName("is_archived")] public bool IsArchived { get; set; }
    [JsonPropertyName("is_favorite")] public bool IsFavorite { get; set; }
    [JsonPropertyName("position")] public double Position { get; set; }
    [JsonPropertyName("views")] public List<VView>? Views { get; set; }

    /// <summary>Vikunja uses negative ids for saved filters shown as projects.</summary>
    [JsonIgnore] public bool IsSavedFilter => Id < 0;

    public VView? KanbanView => Views?.FirstOrDefault(v => v.ViewKind == "kanban");
    public VView? ListView => Views?.FirstOrDefault(v => v.ViewKind == "list");

    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

/// <summary>A view of a project: list, gantt, table or kanban.</summary>
public sealed class VView
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("project_id")] public long ProjectId { get; set; }
    [JsonPropertyName("view_kind")] public string ViewKind { get; set; } = "";
    [JsonPropertyName("default_bucket_id")] public long DefaultBucketId { get; set; }
    [JsonPropertyName("done_bucket_id")] public long DoneBucketId { get; set; }
}

/// <summary>A Kanban column. Returned with its tasks by the view-tasks endpoint.</summary>
public sealed class VBucket
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("project_view_id")] public long ProjectViewId { get; set; }
    [JsonPropertyName("position")] public double Position { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("tasks")] public List<VTask>? Tasks { get; set; }
}

public sealed class VLabel
{
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("hex_color")] public string HexColor { get; set; } = "";
}

/// <summary>
/// A Vikunja task. Vikunja's update endpoint replaces every field, so unknown fields
/// are kept in <see cref="Extra"/> and sent back unchanged on save.
/// </summary>
public sealed partial class VTask
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("project_id")] public long ProjectId { get; set; }
    [JsonPropertyName("done")] public bool Done { get; set; }
    [JsonPropertyName("done_at")] public DateTimeOffset DoneAt { get; set; }
    [JsonPropertyName("due_date")] public DateTimeOffset DueDate { get; set; }
    [JsonPropertyName("start_date")] public DateTimeOffset StartDate { get; set; }
    [JsonPropertyName("end_date")] public DateTimeOffset EndDate { get; set; }
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("percent_done")] public double PercentDone { get; set; }
    [JsonPropertyName("hex_color")] public string HexColor { get; set; } = "";
    [JsonPropertyName("identifier")] public string Identifier { get; set; } = "";
    [JsonPropertyName("index")] public long Index { get; set; }
    [JsonPropertyName("is_favorite")] public bool IsFavorite { get; set; }
    [JsonPropertyName("bucket_id")] public long BucketId { get; set; }
    [JsonPropertyName("position")] public double Position { get; set; }
    [JsonPropertyName("labels")] public List<VLabel>? Labels { get; set; }
    [JsonPropertyName("created")] public DateTimeOffset Created { get; set; }
    [JsonPropertyName("updated")] public DateTimeOffset Updated { get; set; }

    [JsonPropertyName("reminders")] public List<VReminder>? Reminders { get; set; }
    [JsonPropertyName("repeat_after")] public long RepeatAfter { get; set; }
    [JsonPropertyName("repeat_mode")] public int RepeatMode { get; set; }
    [JsonPropertyName("attachments")] public List<VAttachment>? Attachments { get; set; }

    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }

    /// <summary>Vikunja sends 0001-01-01 for "no date".</summary>
    [JsonIgnore] public DateTimeOffset? Due => VDates.OrNull(DueDate);

    /// <summary>Overdue from the day after the due date, as in Microsoft To Do (a task due today is never overdue today).</summary>
    public bool IsOverdue(DateTimeOffset now) => !Done && Due is { } d && d.ToOffset(now.Offset).Date < now.Date;
}

public static class VDates
{
    public static DateTimeOffset? OrNull(DateTimeOffset d) => d.Year <= 1 ? null : d;
    public static DateTimeOffset Empty => new(1, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
