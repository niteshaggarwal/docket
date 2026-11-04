using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Docket.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Docket.Mcp;

/// <summary>
/// The same tools as plain REST, for AIs that use OpenAPI instead of MCP (ChatGPT custom GPT actions,
/// Gemini, scripts). POST /api/tools/{name} with a JSON object of arguments; the reply is {"result": "..."}.
/// GET /openapi.json describes every tool, generated from the MCP tool attributes so the two never drift.
/// </summary>
public static class ActionsApi
{
    private sealed record Tool(string Name, string Title, string Description, bool ReadOnly, MethodInfo Method);

    private static readonly Tool[] Tools = typeof(ActionsApi).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Select(m => (m, a: m.GetCustomAttribute<McpServerToolAttribute>()))
        .Where(x => x.a is not null)
        .Select(x => new Tool(x.a!.Name ?? x.m.Name, x.a.Title ?? x.a.Name ?? x.m.Name,
            x.m.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "", x.a.ReadOnly, x.m))
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToArray();

    private static readonly JsonSerializerOptions Indented = new(JsonSerializerOptions.Default) { WriteIndented = true };

    public static IReadOnlyList<string> ToolNames => Tools.Select(t => t.Name).ToArray();

    private static bool Injected(Type t) => t == typeof(DocketService) || t == typeof(ConnectorOptions) || t == typeof(CancellationToken);

    public static void Map(WebApplication app, Func<HttpContext, bool> authorized)
    {
        app.MapGet("/openapi.json", (HttpContext c) => Results.Text(Spec(RemoteAuth.BaseUrl(c)).ToJsonString(Indented), "application/json"));

        app.MapPost("/api/tools/{name}", async (string name, HttpContext c, DocketService svc, ConnectorOptions opts) =>
        {
            if (!authorized(c)) { await RemoteAuth.Challenge(c); return Results.Empty; }
            var tool = Tools.FirstOrDefault(t => t.Name == name);
            if (tool is null) return Results.Json(new { error = $"No tool called {name}. See /openapi.json." }, statusCode: 404);

            JsonElement body = default;
            if (c.Request.ContentLength is > 0 || c.Request.Headers.TransferEncoding.Count > 0)
            {
                try { body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, cancellationToken: c.RequestAborted); }
                catch (JsonException) { return Results.Json(new { error = "The body must be a JSON object of arguments." }, statusCode: 400); }
            }

            var args = new List<object?>();
            foreach (var p in tool.Method.GetParameters())
            {
                if (p.ParameterType == typeof(DocketService)) { args.Add(svc); continue; }
                if (p.ParameterType == typeof(ConnectorOptions)) { args.Add(opts); continue; }
                if (p.ParameterType == typeof(CancellationToken)) { args.Add(c.RequestAborted); continue; }
                if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty(p.Name!, out var v) && v.ValueKind != JsonValueKind.Null)
                {
                    try { args.Add(v.Deserialize(p.ParameterType)); }
                    catch (JsonException) { return Results.Json(new { error = $"Argument {p.Name} has the wrong type." }, statusCode: 400); }
                }
                else if (p.HasDefaultValue) args.Add(p.DefaultValue);
                else return Results.Json(new { error = $"Missing argument: {p.Name}." }, statusCode: 400);
            }

            try
            {
                var result = await (Task<string>)tool.Method.Invoke(null, [.. args])!;
                return Results.Json(new { result });
            }
            catch (McpException ex) { return Results.Json(new { error = ex.Message }, statusCode: 422); }
            catch (TargetInvocationException ex) when (ex.InnerException is McpException m) { return Results.Json(new { error = m.Message }, statusCode: 422); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or VikunjaException or DocketException)
            { return Results.Json(new { error = "Docket couldn't reach Vikunja or Vikunja refused the request." }, statusCode: 502); }
        });
    }

    /// <summary>OpenAPI 3.1 for every tool. Kept under 30 operations, ChatGPT's limit for one action.</summary>
    public static JsonObject Spec(string baseUrl)
    {
        var paths = new JsonObject();
        foreach (var t in Tools)
        {
            var props = new JsonObject();
            var required = new JsonArray();
            foreach (var p in t.Method.GetParameters().Where(p => !Injected(p.ParameterType)))
            {
                var schema = Schema(p.ParameterType);
                if (p.GetCustomAttribute<DescriptionAttribute>()?.Description is { } d) schema["description"] = d;
                if (p.HasDefaultValue && p.DefaultValue is not null) schema["default"] = JsonSerializer.SerializeToNode(p.DefaultValue, p.DefaultValue.GetType(), Indented);
                props[p.Name!] = schema;
                if (!p.HasDefaultValue) required.Add(p.Name);
            }
            var bodySchema = new JsonObject { ["type"] = "object", ["properties"] = props };
            if (required.Count > 0) bodySchema["required"] = required;

            paths[$"/api/tools/{t.Name}"] = new JsonObject
            {
                ["post"] = new JsonObject
                {
                    ["operationId"] = t.Name,
                    ["summary"] = t.Title,
                    // ChatGPT trims descriptions over 300 characters.
                    ["description"] = t.Description.Length > 300 ? t.Description[..297] + "..." : t.Description,
                    ["x-openai-isConsequential"] = !t.ReadOnly,
                    ["requestBody"] = new JsonObject
                    {
                        ["required"] = required.Count > 0,
                        ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = bodySchema } },
                    },
                    ["responses"] = new JsonObject
                    {
                        ["200"] = Response("The result as readable text, with task ids as #123.", true),
                        ["400"] = Response("Bad arguments.", false),
                        ["401"] = Response("Missing or wrong key.", false),
                        ["422"] = Response("Docket couldn't do it; error says why.", false),
                    },
                },
            };
        }

        return new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "Docket tasks",
                ["version"] = typeof(ActionsApi).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                ["description"] = "Read and change the user's Docket (Vikunja) tasks: My Day, important, planned, lists, steps, reminders, repeats. Nothing here deletes.",
            },
            ["servers"] = new JsonArray(new JsonObject { ["url"] = baseUrl }),
            ["paths"] = paths,
            ["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject { ["key"] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer" } },
                ["schemas"] = new JsonObject(),
            },
            ["security"] = new JsonArray(new JsonObject { ["key"] = new JsonArray() }),
        };
    }

    private static JsonObject Response(string description, bool ok) => new()
    {
        ["description"] = description,
        ["content"] = new JsonObject
        {
            ["application/json"] = new JsonObject
            {
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { [ok ? "result" : "error"] = new JsonObject { ["type"] = "string" } },
                },
            },
        },
    };

    private static JsonObject Schema(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(string)) return new() { ["type"] = "string" };
        if (t == typeof(bool)) return new() { ["type"] = "boolean" };
        if (t == typeof(int) || t == typeof(long)) return new() { ["type"] = "integer" };
        if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return new() { ["type"] = "number" };
        if (t.IsArray) return new() { ["type"] = "array", ["items"] = Schema(t.GetElementType()!) };
        return new() { ["type"] = "string" };
    }
}
