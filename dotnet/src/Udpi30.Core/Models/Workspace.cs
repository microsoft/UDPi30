using System.Text.RegularExpressions;

namespace Udpi30.Core.Models;

/// <summary>Fabric capacity configuration.</summary>
public sealed partial class CapacityConfig
{
    public string? Sku { get; set; }
    public string? CapacityId { get; set; }

    public void Validate()
    {
        if (string.IsNullOrEmpty(Sku) && string.IsNullOrEmpty(CapacityId))
        {
            throw new ValidationException("Either 'sku' or 'capacity_id' must be provided");
        }
    }
}

/// <summary>Git integration settings for the workspace.</summary>
public sealed class GitIntegrationConfig
{
    public string Provider { get; set; } = "azuredevops";
    public string? Organization { get; set; }
    public string? Project { get; set; }
    public string? Repository { get; set; }
    public string Branch { get; set; } = "main";
    public string Directory { get; set; } = "/";
}

/// <summary>Workspace-level configuration.</summary>
public sealed partial class WorkspaceConfig
{
    public string? Name { get; set; }
    public string? WorkspaceId { get; set; }
    public string? CapacityId { get; set; }

    /// <summary>Deprecated: use <see cref="CapacityId"/> instead.</summary>
    public string? Capacity { get; set; }
    public string? Description { get; set; }
    public GitIntegrationConfig? GitIntegration { get; set; }

    /// <summary>
    /// When true, deployed items are organized into Fabric workspace folders by
    /// type (Notebooks, Pipelines, Lakehouses, Reports, Models, Databases, …).
    /// A per-resource <c>folder</c> always takes precedence over the type folder.
    /// Null/false leaves items at the workspace root (the default).
    /// </summary>
    public bool? FoldersByType { get; set; }

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex GuidPattern();

    public void Validate()
    {
        if (!string.IsNullOrEmpty(CapacityId)
            && !CapacityId.StartsWith("${", StringComparison.Ordinal)
            && !GuidPattern().IsMatch(CapacityId))
        {
            throw new ValidationException(
                $"capacity_id '{CapacityId}' is not a valid GUID format. Expected: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx");
        }
    }

    /// <summary>Return capacity_id, falling back to capacity for backwards compat.</summary>
    public string? EffectiveCapacityId => !string.IsNullOrEmpty(CapacityId) ? CapacityId : Capacity;
}
