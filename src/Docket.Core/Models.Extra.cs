using System.Text.Json.Serialization;

namespace Docket.Core;

public sealed class VUser
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public sealed class VComment
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("comment")] public string Comment { get; set; } = "";
    [JsonPropertyName("author")] public VUser? Author { get; set; }
    [JsonPropertyName("created")] public DateTimeOffset Created { get; set; }
}

/// <summary>A reminder: an absolute time, or relative to due/start/end date (seconds, negative = before).</summary>
public sealed class VReminder
{
    [JsonPropertyName("reminder")] public DateTimeOffset Reminder { get; set; }
    [JsonPropertyName("relative_period")] public long RelativePeriod { get; set; }
    [JsonPropertyName("relative_to")] public string RelativeTo { get; set; } = "";
}

public sealed class VFile
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mime")] public string Mime { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

public sealed class VAttachment
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("task_id")] public long TaskId { get; set; }
    [JsonPropertyName("file")] public VFile File { get; set; } = new();
    [JsonPropertyName("created")] public DateTimeOffset Created { get; set; }
}

public partial class VTask
{
    /// <summary>The next reminder that hasn't fired yet, if any.</summary>
    public DateTimeOffset? NextReminder(DateTimeOffset now) =>
        Reminders?.Select(r => VDates.OrNull(r.Reminder)).Where(r => r is not null && r > now).Min();

    public bool HasLabel(string title) => Labels?.Any(l => l.Title.Equals(title, StringComparison.OrdinalIgnoreCase)) == true;
}
