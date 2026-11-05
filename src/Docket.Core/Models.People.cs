using System.Text.Json.Serialization;

namespace Docket.Core;

public partial class VTask
{
    [JsonPropertyName("assignees")] public List<VUser>? Assignees { get; set; }

    /// <summary>Relations by kind: "subtask", "parenttask", "related" and so on. A task may have several parents.</summary>
    [JsonPropertyName("related_tasks")] public Dictionary<string, List<VTask>?>? RelatedTasks { get; set; }

    [JsonPropertyName("created_by")] public VUser? CreatedBy { get; set; }

    [JsonIgnore] public IReadOnlyList<VTask> Subtasks => RelatedTasks?.GetValueOrDefault("subtask") ?? [];
    [JsonIgnore] public IReadOnlyList<VTask> Parents => RelatedTasks?.GetValueOrDefault("parenttask") ?? [];
}

/// <summary>Sharing permission, as Vikunja numbers it.</summary>
public enum SharePermission { Read = 0, ReadWrite = 1, Admin = 2 }

/// <summary>A person a list is shared with.</summary>
public sealed class VProjectUser
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("permission")] public SharePermission Permission { get; set; }
}

public sealed class VTeam
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("members")] public List<VUser>? Members { get; set; }
    [JsonPropertyName("permission")] public SharePermission Permission { get; set; }
}

/// <summary>A public link to a list. Anyone with the link can open it, with the given permission.</summary>
public sealed class VLinkShare
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("permission")] public SharePermission Permission { get; set; }
    /// <summary>1 = no password, 2 = with password.</summary>
    [JsonPropertyName("sharing_type")] public int SharingType { get; set; }
    [JsonPropertyName("created")] public DateTimeOffset Created { get; set; }
}

public static class Permissions
{
    public static string Describe(SharePermission p) => p switch
    {
        SharePermission.Read => "Can view",
        SharePermission.ReadWrite => "Can edit",
        _ => "Can edit and share",
    };
}
