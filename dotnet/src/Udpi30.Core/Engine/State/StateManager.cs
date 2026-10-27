using System.Text.Json;
using System.Text.Json.Nodes;

namespace Udpi30.Core.Engine.State;

/// <summary>
/// Manages deployment-state persistence and drift detection, backed by a
/// pluggable <see cref="IStateBackend"/>. Mirrors <c>StateManager</c> in
/// <c>engine/state.py</c>; state files stay compatible with the Python format.
/// </summary>
public sealed class StateManager
{
    private readonly string _stateDir;
    private readonly string _stateFile;
    private readonly IStateBackend _backend;
    private readonly string _stateKey;
    private readonly string _lockKey;

    public string TargetName { get; }

    public StateManager(string projectDir, string targetName = "default",
        string backendType = "local", IReadOnlyDictionary<string, string>? backendConfig = null)
    {
        TargetName = targetName;
        _stateDir = Path.Combine(projectDir, ".udpi30");
        _stateFile = Path.Combine(_stateDir, $"state-{targetName}.json");
        _backend = StateBackendFactory.Create(backendType, backendConfig, projectDir);
        _stateKey = $"state-{targetName}";
        _lockKey = $"lock-{targetName}";
    }

    private static JsonObject ToJsonObject(DeploymentState state) =>
        JsonSerializer.SerializeToNode(state)!.AsObject();

    private static DeploymentState FromJsonObject(JsonObject obj) =>
        obj.Deserialize<DeploymentState>() ?? new DeploymentState();

    /// <summary>Load state from the backend. Returns empty state if none exists.</summary>
    public DeploymentState Load()
    {
        var data = _backend.Read(_stateKey);
        if (data is not null)
        {
            return FromJsonObject(data);
        }
        // Fallback: local file (migration from older format).
        if (File.Exists(_stateFile))
        {
            try
            {
                var obj = JsonNode.Parse(File.ReadAllText(_stateFile)) as JsonObject;
                if (obj is not null)
                {
                    return FromJsonObject(obj);
                }
            }
            catch (JsonException)
            {
                // Ignore corrupt local file.
            }
        }
        return new DeploymentState { TargetName = TargetName };
    }

    /// <summary>Persist state to the backend (and a local copy for compatibility).</summary>
    public void Save(DeploymentState state)
    {
        _backend.Write(_stateKey, ToJsonObject(state));
        Directory.CreateDirectory(_stateDir);
        File.WriteAllText(_stateFile, JsonSerializer.Serialize(state, StateJson.Options));

        var gitignore = Path.Combine(_stateDir, ".gitignore");
        if (!File.Exists(gitignore))
        {
            File.WriteAllText(gitignore, "# Deployment state — machine-specific, do not commit\n*\n");
        }
    }

    /// <summary>Record a successful deployment and append a history entry.</summary>
    public DeploymentState RecordDeployment(string deploymentName, string deploymentVersion,
        string workspaceId, string workspaceName, IReadOnlyDictionary<string, Dictionary<string, object?>> deployedItems)
    {
        var state = Load();
        state.DeploymentName = deploymentName;
        state.DeploymentVersion = deploymentVersion;
        state.WorkspaceId = workspaceId;
        state.WorkspaceName = workspaceName;
        state.LastDeployed = BackendUtil.EpochSeconds();
        state.TargetName = TargetName;

        foreach (var (key, info) in deployedItems)
        {
            state.Resources[key] = new ResourceState
            {
                ItemId = info.GetValueOrDefault("id")?.ToString() ?? "",
                ItemType = info.GetValueOrDefault("type")?.ToString() ?? "",
                ResourceKey = key,
                DefinitionHash = info.GetValueOrDefault("definition_hash")?.ToString(),
                LastDeployed = BackendUtil.EpochSeconds(),
                Properties = info.GetValueOrDefault("properties") as Dictionary<string, object?> ?? [],
            };
        }

        Save(state);
        RecordHistory(state);
        return state;
    }

    /// <summary>Remove a resource from state (after deletion).</summary>
    public void RemoveResource(string resourceKey)
    {
        var state = Load();
        state.Resources.Remove(resourceKey);
        Save(state);
    }

    public bool AcquireLock(string deployerId = "", int timeoutMinutes = 30)
    {
        var owner = string.IsNullOrEmpty(deployerId)
            ? $"{Environment.GetEnvironmentVariable("USER") ?? Environment.UserName}@{Environment.MachineName}"
            : deployerId;
        var ciRun = Environment.GetEnvironmentVariable("GITHUB_RUN_ID")
            ?? Environment.GetEnvironmentVariable("BUILD_BUILDID") ?? "";
        if (!string.IsNullOrEmpty(ciRun))
        {
            owner = $"{owner} (CI: {ciRun})";
        }
        return _backend.AcquireLock(_lockKey, owner, timeoutMinutes * 60);
    }

    public void ReleaseLock() => _backend.ReleaseLock(_lockKey);

    public JsonObject? GetLockInfo() => _backend.GetLockInfo(_lockKey);

    /// <summary>
    /// Compare state against live workspace items. Returns resource_key -> drift_type
    /// ("added", "removed", or "modified").
    /// </summary>
    public Dictionary<string, string> DetectDrift(IReadOnlyDictionary<string, Dictionary<string, object?>> liveItems)
    {
        var state = Load();
        var drift = new Dictionary<string, string>();

        var stateKeys = state.Resources.Keys.ToHashSet();
        var liveKeys = liveItems.Keys.ToHashSet();

        foreach (var key in liveKeys.Except(stateKeys))
        {
            drift[key] = "added";
        }
        foreach (var key in stateKeys.Except(liveKeys))
        {
            drift[key] = "removed";
        }
        foreach (var key in stateKeys.Intersect(liveKeys))
        {
            var stateRes = state.Resources[key];
            var liveId = liveItems[key].GetValueOrDefault("id")?.ToString() ?? "";
            if (stateRes.ItemId != liveId)
            {
                drift[key] = "modified";
            }
        }

        return drift;
    }

    /// <summary>Record a deployment in the history log.</summary>
    public void RecordHistory(DeploymentState state, string summary = "")
    {
        var historyDir = Path.Combine(_stateDir, "history");
        Directory.CreateDirectory(historyDir);

        var deployId = ((long)state.LastDeployed).ToString();
        var resources = new JsonObject();
        foreach (var (k, v) in state.Resources)
        {
            resources[k] = new JsonObject { ["item_id"] = v.ItemId, ["type"] = v.ItemType };
        }

        var entry = new JsonObject
        {
            ["deploy_id"] = deployId,
            ["timestamp"] = state.LastDeployed,
            ["deployment_name"] = state.DeploymentName,
            ["deployment_version"] = state.DeploymentVersion,
            ["target"] = state.TargetName,
            ["workspace_id"] = state.WorkspaceId,
            ["resource_count"] = state.Resources.Count,
            ["summary"] = summary,
            ["resources"] = resources,
        };

        var filename = $"{deployId}-{state.TargetName}.json";
        File.WriteAllText(Path.Combine(historyDir, filename),
            entry.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        try
        {
            _backend.Write($"history-{filename.Replace(".json", "")}", entry);
        }
        catch
        {
            // Best-effort for remote history.
        }
    }

    /// <summary>List deployment history entries, most recent first.</summary>
    public List<JsonObject> ListHistory(int limit = 20)
    {
        var historyDir = Path.Combine(_stateDir, "history");
        if (!Directory.Exists(historyDir))
        {
            return [];
        }

        var entries = new List<JsonObject>();
        foreach (var f in Directory.GetFiles(historyDir, "*.json").OrderByDescending(p => p, StringComparer.Ordinal).Take(limit))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(f)) is JsonObject obj)
                {
                    entries.Add(obj);
                }
            }
            catch (JsonException)
            {
                // Skip corrupt entry.
            }
        }
        return entries;
    }

    /// <summary>Get a specific deployment history entry by id prefix.</summary>
    public JsonObject? GetHistoryEntry(string deployId)
    {
        var historyDir = Path.Combine(_stateDir, "history");
        if (!Directory.Exists(historyDir))
        {
            return null;
        }
        foreach (var f in Directory.GetFiles(historyDir, $"{deployId}*.json"))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(f)) is JsonObject obj)
                {
                    return obj;
                }
            }
            catch (JsonException)
            {
                // Skip.
            }
        }
        return null;
    }
}
