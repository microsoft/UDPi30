using System.Text.Json;
using System.Text.Json.Nodes;

namespace Udpi30.Core.Engine;

/// <summary>Logs deployment actions to a structured JSONL audit file. Mirrors <c>engine/audit.py</c>.</summary>
public sealed class AuditLogger
{
    private readonly string _logDir;
    private readonly string _logFile;
    private readonly string _target;

    public AuditLogger(string projectDir, string target = "default")
    {
        _logDir = Path.Combine(projectDir, ".udpi30");
        _logFile = Path.Combine(_logDir, "audit.jsonl");
        _target = target;
    }

    public void Log(string action, string resource = "", string resourceType = "",
        string status = "success", IReadOnlyDictionary<string, object?>? details = null)
    {
        Directory.CreateDirectory(_logDir);

        var now = DateTimeOffset.UtcNow;
        var entry = new JsonObject
        {
            ["timestamp"] = now.ToUnixTimeMilliseconds() / 1000.0,
            ["iso_time"] = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["action"] = action,
            ["resource"] = resource,
            ["resource_type"] = resourceType,
            ["target"] = _target,
            ["status"] = status,
            ["deployer"] = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName ?? "unknown",
            ["ci_run_id"] = Environment.GetEnvironmentVariable("GITHUB_RUN_ID")
                ?? Environment.GetEnvironmentVariable("BUILD_BUILDID") ?? "",
        };
        if (details is { Count: > 0 })
        {
            entry["details"] = JsonSerializer.SerializeToNode(details);
        }

        File.AppendAllText(_logFile, entry.ToJsonString() + "\n");
    }

    public List<JsonObject> GetEntries(int limit = 100)
    {
        if (!File.Exists(_logFile))
        {
            return [];
        }
        var entries = new List<JsonObject>();
        foreach (var line in File.ReadAllText(_logFile).Trim().Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            try
            {
                if (JsonNode.Parse(line) is JsonObject obj)
                {
                    entries.Add(obj);
                }
            }
            catch (JsonException)
            {
                // Skip malformed line.
            }
        }
        return entries.TakeLast(limit).ToList();
    }
}
