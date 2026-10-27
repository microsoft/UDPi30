using Spectre.Console;
using Udpi30.Core.Engine.State;
using Udpi30.Core.Models;
using Udpi30.Core.Yaml;

namespace Udpi30.Core.Engine;

public enum PlanAction
{
    [EnumValue("create")] Create,
    [EnumValue("update")] Update,
    [EnumValue("delete")] Delete,
    [EnumValue("no_change")] NoChange,
    [EnumValue("replace")] Replace,
}

/// <summary>A single item in the deployment plan.</summary>
public sealed class PlanItem
{
    public required string ResourceKey { get; init; }
    public required string ResourceType { get; init; }
    public required PlanAction Action { get; init; }
    public string Reason { get; init; } = "";
    public Dictionary<string, object?> Details { get; init; } = [];
    public List<string> DependsOn { get; init; } = [];

    public string Symbol => Action switch
    {
        PlanAction.Create => "+",
        PlanAction.Update => "~",
        PlanAction.Delete => "-",
        PlanAction.NoChange => "=",
        PlanAction.Replace => "!",
        _ => "?",
    };

    public string Color => Action switch
    {
        PlanAction.Create => "green",
        PlanAction.Update => "yellow",
        PlanAction.Delete => "red",
        PlanAction.NoChange => "dim",
        PlanAction.Replace => "magenta",
        _ => "default",
    };

    public string ActionValue => EnumValueMapValue(Action);

    private static string EnumValueMapValue(PlanAction action) => action switch
    {
        PlanAction.Create => "create",
        PlanAction.Update => "update",
        PlanAction.Delete => "delete",
        PlanAction.NoChange => "no_change",
        PlanAction.Replace => "replace",
        _ => "unknown",
    };
}

/// <summary>Complete deployment plan.</summary>
public sealed class DeploymentPlan
{
    public required string DeploymentName { get; init; }
    public required string TargetName { get; init; }
    public string? WorkspaceName { get; init; }
    public List<PlanItem> Items { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool HasChanges => Items.Any(i => i.Action != PlanAction.NoChange);
    public List<PlanItem> Creates => Items.Where(i => i.Action == PlanAction.Create).ToList();
    public List<PlanItem> Updates => Items.Where(i => i.Action == PlanAction.Update).ToList();
    public List<PlanItem> Deletes => Items.Where(i => i.Action == PlanAction.Delete).ToList();
    public List<PlanItem> Replaces => Items.Where(i => i.Action == PlanAction.Replace).ToList();

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Creates.Count > 0) parts.Add($"{Creates.Count} to create");
            if (Updates.Count > 0) parts.Add($"{Updates.Count} to update");
            if (Deletes.Count > 0) parts.Add($"{Deletes.Count} to delete");
            if (Replaces.Count > 0) parts.Add($"{Replaces.Count} to replace");
            var noChange = Items.Count(i => i.Action == PlanAction.NoChange);
            if (noChange > 0) parts.Add($"{noChange} unchanged");
            return parts.Count > 0 ? string.Join(", ", parts) : "No resources defined";
        }
    }

    private static readonly IReadOnlyDictionary<string, string> CuHints = new Dictionary<string, string>
    {
        ["Lakehouse"] = "~0.5 CU/hr active",
        ["Warehouse"] = "~1 CU/hr active",
        ["Notebook"] = "~0.5 CU/hr running",
        ["DataPipeline"] = "~0.25 CU/hr running",
        ["SemanticModel"] = "~0.5 CU/hr loaded",
        ["Eventhouse"] = "~1 CU/hr active",
    };

    /// <summary>Pretty-print the plan to the console using Spectre.Console.</summary>
    public void Display(IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;

        console.WriteLine();
        console.MarkupLine($"[bold]Deployment Plan: {Markup.Escape(DeploymentName)}[/]");
        console.MarkupLine($"  Target:    {Markup.Escape(TargetName)}");
        if (!string.IsNullOrEmpty(WorkspaceName))
        {
            console.MarkupLine($"  Workspace: {Markup.Escape(WorkspaceName)}");
        }
        console.WriteLine();

        if (Errors.Count > 0)
        {
            foreach (var error in Errors)
            {
                console.MarkupLine($"  [red]ERROR:[/] {Markup.Escape(error)}");
            }
            console.WriteLine();
            return;
        }

        if (Warnings.Count > 0)
        {
            foreach (var warning in Warnings)
            {
                console.MarkupLine($"  [yellow]WARNING:[/] {Markup.Escape(warning)}");
            }
            console.WriteLine();
        }

        if (!HasChanges)
        {
            console.MarkupLine("  [dim]No changes detected. Infrastructure is up to date.[/]");
            return;
        }

        var table = new Table().NoBorder().HideHeaders();
        table.AddColumn(new TableColumn(""));
        table.AddColumn(new TableColumn("Resource"));
        table.AddColumn(new TableColumn("Type"));
        table.AddColumn(new TableColumn("Action"));
        table.AddColumn(new TableColumn("Details"));

        foreach (var item in Items)
        {
            if (item.Action == PlanAction.NoChange)
            {
                continue;
            }
            table.AddRow(
                $"[{item.Color}]{item.Symbol}[/]",
                $"[{item.Color}]{Markup.Escape(item.ResourceKey)}[/]",
                Markup.Escape(item.ResourceType),
                $"[{item.Color}]{item.ActionValue}[/]",
                Markup.Escape(item.Reason));
        }

        console.Write(table);
        console.WriteLine();
        console.MarkupLine($"  [bold]Summary:[/] {Markup.Escape(Summary)}");
        console.WriteLine();

        var resourceCounts = new Dictionary<string, int>();
        foreach (var item in Items)
        {
            if (item.Action is PlanAction.Create or PlanAction.Update)
            {
                resourceCounts[item.ResourceType] = resourceCounts.GetValueOrDefault(item.ResourceType) + 1;
            }
        }
        if (resourceCounts.Keys.Any(CuHints.ContainsKey))
        {
            console.MarkupLine("  [dim]Estimated capacity usage (informational):[/]");
            foreach (var (rt, count) in resourceCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (CuHints.TryGetValue(rt, out var hint))
                {
                    console.MarkupLine($"    [dim]{count}x {Markup.Escape(rt)}: {Markup.Escape(hint)}[/]");
                }
            }
            console.WriteLine();
        }
    }
}

public static class Planner
{
    /// <summary>
    /// Create a deployment plan by comparing the deployment definition to the
    /// current workspace items. Mirrors <c>planner.create_plan</c>.
    /// </summary>
    public static DeploymentPlan CreatePlan(
        DeploymentDefinition deployment,
        string? targetName = null,
        IReadOnlyDictionary<string, Dictionary<string, object?>>? workspaceItems = null,
        bool autoDelete = false,
        DeploymentState? state = null)
    {
        var workspace = deployment.GetEffectiveWorkspace(targetName);
        workspaceItems ??= new Dictionary<string, Dictionary<string, object?>>();

        var plan = new DeploymentPlan
        {
            DeploymentName = deployment.Deployment.Name,
            TargetName = targetName ?? "(default)",
            WorkspaceName = workspace.Name,
        };

        List<ResourceNode> orderedNodes;
        try
        {
            orderedNodes = Resolver.GetDeploymentOrder(deployment);
        }
        catch (Exception e)
        {
            plan.Errors.Add(e.Message);
            return plan;
        }

        var accountedItems = new HashSet<string>();

        foreach (var node in orderedNodes)
        {
            var udpType = ResourceTypeRegistry.ItemTypeMap.GetValueOrDefault(node.ResourceType, node.ResourceType);
            var exists = workspaceItems.ContainsKey(node.Key);

            if (exists)
            {
                accountedItems.Add(node.Key);
                if (state is not null
                    && state.Resources.TryGetValue(node.Key, out var res)
                    && !string.IsNullOrEmpty(res.DefinitionHash))
                {
                    plan.Items.Add(new PlanItem
                    {
                        ResourceKey = node.Key,
                        ResourceType = udpType,
                        Action = PlanAction.NoChange,
                        Reason = "Matches deployed state",
                        DependsOn = node.DependsOn.ToList(),
                    });
                    continue;
                }
                plan.Items.Add(new PlanItem
                {
                    ResourceKey = node.Key,
                    ResourceType = udpType,
                    Action = PlanAction.Update,
                    Reason = "Definition updated",
                    DependsOn = node.DependsOn.ToList(),
                });
            }
            else
            {
                plan.Items.Add(new PlanItem
                {
                    ResourceKey = node.Key,
                    ResourceType = udpType,
                    Action = PlanAction.Create,
                    Reason = "New resource",
                    DependsOn = node.DependsOn.ToList(),
                });
            }
        }

        if (autoDelete)
        {
            foreach (var (itemName, itemInfo) in workspaceItems)
            {
                if (!accountedItems.Contains(itemName))
                {
                    plan.Items.Add(new PlanItem
                    {
                        ResourceKey = itemName,
                        ResourceType = itemInfo.GetValueOrDefault("type")?.ToString() ?? "Unknown",
                        Action = PlanAction.Delete,
                        Reason = "Not in deployment definition",
                    });
                }
            }
        }
        else
        {
            var unmanaged = workspaceItems.Keys.Where(k => !accountedItems.Contains(k)).OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (unmanaged.Count > 0)
            {
                var preview = string.Join(", ", unmanaged.Take(5));
                plan.Warnings.Add(
                    $"{unmanaged.Count} workspace item(s) not managed by this deployment: " +
                    preview + (unmanaged.Count > 5 ? "..." : ""));
            }
        }

        return plan;
    }
}
