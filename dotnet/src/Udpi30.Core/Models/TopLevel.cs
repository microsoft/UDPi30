namespace Udpi30.Core.Models;

/// <summary>Top-level deployment metadata.</summary>
public sealed class DeploymentMetadata
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.1.0";
    public string? Description { get; set; }
    public List<string> DependsOn { get; set; } = [];
}

/// <summary>A single notification target.</summary>
public sealed class NotificationConfig
{
    public string Type { get; set; } = "";
    public string Webhook { get; set; } = "";
    public string Message { get; set; } = "Deployed {deployment.name} v{deployment.version} to {target}";
}

/// <summary>Notifications configuration.</summary>
public sealed class NotificationsConfig
{
    public List<NotificationConfig> OnSuccess { get; set; } = [];
    public List<NotificationConfig> OnFailure { get; set; } = [];
}

/// <summary>A single policy rule for validation.</summary>
public sealed class PolicyRule
{
    public string Name { get; set; } = "";
    public string Check { get; set; } = "";
    public object? Value { get; set; }
    public string Severity { get; set; } = "error";
}

/// <summary>Policy enforcement configuration.</summary>
public sealed class PolicyConfig
{
    public List<PolicyRule> Rules { get; set; } = [];
    public bool RequireDescription { get; set; }
    public string? NamingConvention { get; set; }
    public int? MaxNotebookSizeKb { get; set; }
    public List<string> BlockedLibraries { get; set; } = [];
}

/// <summary>Remote state backend configuration.</summary>
public sealed class StateConfig
{
    public string Backend { get; set; } = "local";
    public Dictionary<string, string> Config { get; set; } = [];
}

/// <summary>Include additional YAML files into the deployment definition.</summary>
public sealed class IncludeConfig
{
    public List<string> Paths { get; set; } = [];
}
