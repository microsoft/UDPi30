using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Udpi30.Core.Engine.State;

/// <summary>State of a single deployed resource. Serialized to snake_case JSON.</summary>
public sealed class ResourceState
{
    [JsonPropertyName("item_id")] public string ItemId { get; set; } = "";
    [JsonPropertyName("item_type")] public string ItemType { get; set; } = "";
    [JsonPropertyName("resource_key")] public string ResourceKey { get; set; } = "";
    [JsonPropertyName("definition_hash")] public string? DefinitionHash { get; set; }
    [JsonPropertyName("last_deployed")] public double LastDeployed { get; set; }
    [JsonPropertyName("properties")] public Dictionary<string, object?> Properties { get; set; } = [];
}

/// <summary>Full deployment state for a deployment + target.</summary>
public sealed class DeploymentState
{
    public const int StateVersion = 1;

    [JsonPropertyName("version")] public int Version { get; set; } = StateVersion;
    [JsonPropertyName("deployment_name")] public string DeploymentName { get; set; } = "";
    [JsonPropertyName("deployment_version")] public string DeploymentVersion { get; set; } = "";
    [JsonPropertyName("target_name")] public string TargetName { get; set; } = "";
    [JsonPropertyName("workspace_id")] public string WorkspaceId { get; set; } = "";
    [JsonPropertyName("workspace_name")] public string WorkspaceName { get; set; } = "";
    [JsonPropertyName("last_deployed")] public double LastDeployed { get; set; }
    [JsonPropertyName("resources")] public Dictionary<string, ResourceState> Resources { get; set; } = [];
}

/// <summary>Shared JSON options + helpers for state and history (snake_case, indented).</summary>
public static class StateJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public const string StateFileName = ".udpi30-state.json";

    /// <summary>Compute a hash of an item definition for change detection (16 hex chars).</summary>
    public static string? ComputeDefinitionHash(object? definition)
    {
        if (definition is null)
        {
            return null;
        }
        // Canonical JSON with sorted keys, matching Python's json.dumps(sort_keys=True).
        using var doc = JsonSerializer.SerializeToDocument(definition);
        var sb = new StringBuilder();
        WriteCanonical(doc.RootElement, sb);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(bytes)[..16];
    }

    private static void WriteCanonical(JsonElement el, StringBuilder sb)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                var first = true;
                foreach (var prop in el.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    sb.Append(JsonSerializer.Serialize(prop.Name)).Append(':');
                    WriteCanonical(prop.Value, sb);
                }
                sb.Append('}');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                var firstItem = true;
                foreach (var item in el.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        sb.Append(',');
                    }
                    firstItem = false;
                    WriteCanonical(item, sb);
                }
                sb.Append(']');
                break;
            default:
                sb.Append(el.GetRawText());
                break;
        }
    }
}
