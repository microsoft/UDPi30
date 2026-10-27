using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Udpi30.Core.Engine;

/// <summary>Metrics for a single deployment. Mirrors <c>engine/metrics.py</c>.</summary>
public sealed class DeploymentMetrics
{
    [JsonPropertyName("start_time")] public double StartTime { get; set; }
    [JsonPropertyName("end_time")] public double EndTime { get; set; }
    [JsonPropertyName("duration_seconds")] public double DurationSeconds { get; set; }
    [JsonPropertyName("target")] public string Target { get; set; } = "";
    [JsonPropertyName("items_created")] public int ItemsCreated { get; set; }
    [JsonPropertyName("items_updated")] public int ItemsUpdated { get; set; }
    [JsonPropertyName("items_deleted")] public int ItemsDeleted { get; set; }
    [JsonPropertyName("items_failed")] public int ItemsFailed { get; set; }
    [JsonPropertyName("items_skipped")] public int ItemsSkipped { get; set; }
    [JsonPropertyName("api_calls")] public int ApiCalls { get; set; }
    [JsonPropertyName("success")] public bool Success { get; set; } = true;

    public void Finalize()
    {
        EndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        DurationSeconds = Math.Round(EndTime - StartTime, 2);
    }
}

/// <summary>Collects and persists deployment metrics (keeps the last 100 entries).</summary>
public sealed class MetricsCollector
{
    private readonly string _metricsFile;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public MetricsCollector(string projectDir)
    {
        _metricsFile = Path.Combine(projectDir, ".udpi30", "metrics.json");
    }

    public void Save(DeploymentMetrics metrics)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_metricsFile)!);
        var history = LoadAllNodes();
        history.Add(JsonSerializer.SerializeToNode(metrics)!.AsObject());
        if (history.Count > 100)
        {
            history = history.TakeLast(100).ToList();
        }
        var arr = new JsonArray(history.Select(n => (JsonNode)n.DeepClone()).ToArray());
        File.WriteAllText(_metricsFile, arr.ToJsonString(Options));
    }

    public List<JsonObject> LoadAllNodes()
    {
        if (!File.Exists(_metricsFile))
        {
            return [];
        }
        try
        {
            return (JsonNode.Parse(File.ReadAllText(_metricsFile)) as JsonArray)?
                .OfType<JsonObject>().ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public JsonObject Summary()
    {
        var entries = LoadAllNodes();
        if (entries.Count == 0)
        {
            return new JsonObject { ["total_deploys"] = 0 };
        }

        var successes = entries.Where(e => e["success"]?.GetValue<bool>() == true).ToList();
        var failures = entries.Where(e => e["success"]?.GetValue<bool>() != true).ToList();
        var durations = successes.Select(e => e["duration_seconds"]?.GetValue<double>() ?? 0).ToList();

        return new JsonObject
        {
            ["total_deploys"] = entries.Count,
            ["successes"] = successes.Count,
            ["failures"] = failures.Count,
            ["success_rate"] = $"{(double)successes.Count / entries.Count * 100:F0}%",
            ["avg_duration"] = durations.Count > 0 ? $"{durations.Average():F1}s" : "N/A",
            ["total_items_created"] = entries.Sum(e => e["items_created"]?.GetValue<int>() ?? 0),
            ["total_items_updated"] = entries.Sum(e => e["items_updated"]?.GetValue<int>() ?? 0),
        };
    }
}
