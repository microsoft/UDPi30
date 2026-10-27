namespace Udpi30.Core.Models;

/// <summary>Variable with optional default value.</summary>
public sealed class VariableDefinition
{
    public string? Description { get; set; }
    public string? Default { get; set; }
}

/// <summary>
/// A variable entry in the top-level <c>variables</c> map, which in YAML may be
/// either a plain string (the value) or an object with <c>description</c>/<c>default</c>.
/// Mirrors the Python <c>dict[str, VariableDefinition | str]</c> union.
/// </summary>
public sealed class VariableValue
{
    public string? RawString { get; set; }
    public VariableDefinition? Definition { get; set; }

    public static VariableValue FromString(string value) => new() { RawString = value };
    public static VariableValue FromDefinition(VariableDefinition def) => new() { Definition = def };

    /// <summary>The effective default: the raw string, or the definition's default.</summary>
    public string? EffectiveDefault => RawString ?? Definition?.Default;
}

/// <summary>Run-as identity for a target.</summary>
public sealed class RunAsConfig
{
    public string? ServicePrincipal { get; set; }
    public string? UserName { get; set; }
}

/// <summary>Post-deploy validation check.</summary>
public sealed class ValidationCheck
{
    public string? Run { get; set; }
    public string? Sql { get; set; }
    public string? Expect { get; set; }
    public int Timeout { get; set; } = 300;
}

/// <summary>Deployment strategy configuration.</summary>
public sealed class DeploymentStrategy
{
    public string Type { get; set; } = "all-at-once";
    public List<string> CanaryResources { get; set; } = [];
    public ValidationCheck? Validation { get; set; }
}

/// <summary>Per-target resource overrides.</summary>
public sealed class ResourceOverrides
{
    public Dictionary<string, Dictionary<string, object?>> Lakehouses { get; set; } = [];
    public Dictionary<string, Dictionary<string, object?>> Notebooks { get; set; } = [];
    public Dictionary<string, Dictionary<string, object?>> Pipelines { get; set; } = [];
    public Dictionary<string, Dictionary<string, object?>> Warehouses { get; set; } = [];
    public Dictionary<string, Dictionary<string, object?>> SemanticModels { get; set; } = [];
    public Dictionary<string, Dictionary<string, object?>> Reports { get; set; } = [];
    public Dictionary<string, Dictionary<string, object?>> DataAgents { get; set; } = [];
    public Dictionary<string, Dictionary<string, object?>> Environments { get; set; } = [];
    public Dictionary<string, Dictionary<string, object?>> Eventhouses { get; set; } = [];
    public Dictionary<string, Dictionary<string, object?>> Eventstreams { get; set; } = [];
}

/// <summary>Environment target (dev, staging, prod).</summary>
public sealed class TargetConfig
{
    public bool Default { get; set; }
    public WorkspaceConfig? Workspace { get; set; }
    public Dictionary<string, string> Variables { get; set; } = [];
    public RunAsConfig? RunAs { get; set; }
    public SecurityConfig? Security { get; set; }
    public ResourceOverrides? Resources { get; set; }
    public List<ValidationCheck> PostDeploy { get; set; } = [];
    public DeploymentStrategy? DeploymentStrategy { get; set; }
}
