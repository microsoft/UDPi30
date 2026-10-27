namespace Udpi30.Core.Models;

/// <summary>
/// Root model for a <c>udp.yml</c> deployment definition — the single
/// declarative project definition for Microsoft Unified Data Platform.
/// </summary>
public sealed class DeploymentDefinition
{
    public DeploymentMetadata Deployment { get; set; } = new();
    public WorkspaceConfig Workspace { get; set; } = new();
    public List<string> Include { get; set; } = [];
    public string? Extends { get; set; }
    public Dictionary<string, VariableValue> Variables { get; set; } = [];
    public ResourcesConfig Resources { get; set; } = new();
    public AzureConfig Azure { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public Dictionary<string, ConnectionConfig> Connections { get; set; } = [];
    public PolicyConfig Policies { get; set; } = new();
    public NotificationsConfig Notifications { get; set; } = new();
    public StateConfig State { get; set; } = new();
    public AdminConfig Admin { get; set; } = new();
    public Dictionary<string, TargetConfig> Targets { get; set; } = [];

    /// <summary>
    /// Validate cross-resource references and naming rules. Mirrors the Pydantic
    /// <c>validate_references</c> model validator — throws
    /// <see cref="ValidationException"/> with the same messages on failure.
    /// </summary>
    public void ValidateReferences()
    {
        Workspace.Validate();

        var allKeys = Resources.AllResourceKeys();
        var errors = new List<string>();

        foreach (var (key, nb) in Resources.Notebooks)
        {
            if (!string.IsNullOrEmpty(nb.Environment) && !Resources.Environments.ContainsKey(nb.Environment))
            {
                errors.Add($"Notebook '{key}' references unknown environment '{nb.Environment}'");
            }
            if (!string.IsNullOrEmpty(nb.DefaultLakehouse) && !Resources.Lakehouses.ContainsKey(nb.DefaultLakehouse))
            {
                errors.Add($"Notebook '{key}' references unknown lakehouse '{nb.DefaultLakehouse}'");
            }
        }

        foreach (var (key, report) in Resources.Reports)
        {
            if (!string.IsNullOrEmpty(report.SemanticModel) && !Resources.SemanticModels.ContainsKey(report.SemanticModel))
            {
                errors.Add($"Report '{key}' references unknown semantic model '{report.SemanticModel}'");
            }
        }

        foreach (var (key, agent) in Resources.DataAgents)
        {
            foreach (var src in agent.Sources)
            {
                if (!allKeys.Contains(src))
                {
                    errors.Add($"Data Agent '{key}' references unknown source '{src}'");
                }
            }
        }

        foreach (var (key, pipeline) in Resources.Pipelines)
        {
            foreach (var activity in pipeline.Activities)
            {
                if (!string.IsNullOrEmpty(activity.Notebook) && !Resources.Notebooks.ContainsKey(activity.Notebook))
                {
                    errors.Add($"Pipeline '{key}' activity references unknown notebook '{activity.Notebook}'");
                }
                if (!string.IsNullOrEmpty(activity.Pipeline) && !Resources.Pipelines.ContainsKey(activity.Pipeline))
                {
                    errors.Add($"Pipeline '{key}' activity references unknown pipeline '{activity.Pipeline}'");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Deployment validation errors:", errors);
        }

        var nameWarnings = Resources.ValidateResourceNames();
        if (nameWarnings.Count > 0)
        {
            throw new ValidationException("Resource naming errors:", nameWarnings);
        }
    }

    /// <summary>Resolve the target config, falling back to the default target.</summary>
    public TargetConfig ResolveTarget(string? targetName = null)
    {
        if (!string.IsNullOrEmpty(targetName))
        {
            if (!Targets.TryGetValue(targetName, out var named))
            {
                throw new ValidationException(
                    $"Unknown target '{targetName}'. Available: [{string.Join(", ", Targets.Keys)}]");
            }
            return named;
        }

        foreach (var target in Targets.Values)
        {
            if (target.Default)
            {
                return target;
            }
        }

        return new TargetConfig();
    }

    /// <summary>Get the effective workspace config for a target (merged with base).</summary>
    public WorkspaceConfig GetEffectiveWorkspace(string? targetName = null)
    {
        var target = ResolveTarget(targetName);
        var baseWs = Workspace;

        if (target.Workspace is { } tw)
        {
            return new WorkspaceConfig
            {
                Name = tw.Name ?? baseWs.Name,
                WorkspaceId = tw.WorkspaceId ?? baseWs.WorkspaceId,
                CapacityId = tw.CapacityId ?? baseWs.CapacityId,
                Capacity = tw.Capacity ?? baseWs.Capacity,
                Description = tw.Description ?? baseWs.Description,
                GitIntegration = tw.GitIntegration ?? baseWs.GitIntegration,
                FoldersByType = tw.FoldersByType ?? baseWs.FoldersByType,
            };
        }
        return baseWs;
    }

    /// <summary>Resolve variables with target overrides applied.</summary>
    public Dictionary<string, string> ResolveVariables(string? targetName = null)
    {
        var resolved = new Dictionary<string, string>();

        foreach (var (key, val) in Variables)
        {
            if (val.EffectiveDefault is { } d)
            {
                resolved[key] = d;
            }
        }

        var target = ResolveTarget(targetName);
        foreach (var (key, val) in target.Variables)
        {
            resolved[key] = val;
        }

        return resolved;
    }
}
