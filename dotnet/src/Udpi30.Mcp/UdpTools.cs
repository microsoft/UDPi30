using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Engine.State;
using Udpi30.Core.Generators;
using Udpi30.Core.Models;
using static Udpi30.Mcp.McpHelpers;

namespace Udpi30.Mcp;

/// <summary>
/// MCP tools exposing UDPI30 operations for Claude Code, Cursor, etc. Tool names,
/// input parameters, and response shapes mirror <c>mcp_server/server.py</c>.
/// </summary>
[McpServerToolType]
public static class UdpTools
{
    private static IAnsiConsole QuietConsole() => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(TextWriter.Null),
    });

    private static string Guard(Func<string> body)
    {
        try
        {
            return body();
        }
        catch (Exception e)
        {
            return $"Error: {e.Message}\n{e.StackTrace}";
        }
    }

    [McpServerTool(Name = "udp_validate")]
    [Description("Validate a udp.yml deployment definition. Checks schema, references, dependencies, naming rules, and policies.")]
    public static string Validate(
        [Description("Path to project directory containing udp.yml")] string? project_dir = null,
        [Description("Target environment (dev, staging, prod)")] string? target = null) => Guard(() =>
    {
        var deployment = LoadDeployment(project_dir, target);
        var resourceCounts = new Dictionary<string, object?>();
        foreach (var info in ResourceTypeRegistry.All)
        {
            var dict = (System.Collections.IDictionary)typeof(ResourcesConfig).GetProperty(info.PropertyName)!.GetValue(deployment.Resources)!;
            if (dict.Count > 0)
            {
                resourceCounts[info.FieldName] = dict.Count;
            }
        }
        var order = Resolver.GetDeploymentOrder(deployment)
            .Select(n => new Dictionary<string, object?> { ["key"] = n.Key, ["resource_type"] = n.ResourceType, ["depends_on"] = n.DependsOn.ToList() })
            .ToList();

        return Format(new Dictionary<string, object?>
        {
            ["valid"] = true,
            ["deployment_name"] = deployment.Deployment.Name,
            ["version"] = deployment.Deployment.Version,
            ["description"] = deployment.Deployment.Description,
            ["total_resources"] = resourceCounts.Values.Sum(v => (int)v!),
            ["resources"] = resourceCounts,
            ["deployment_order"] = order,
            ["targets"] = deployment.Targets.Keys.ToList(),
        });
    });

    [McpServerTool(Name = "udp_plan")]
    [Description("Preview what would change if deployed. Shows create/update/delete actions without making changes.")]
    public static string Plan(
        [Description("Path to project directory")] string? project_dir = null,
        [Description("Target environment")] string? target = null) => Guard(() =>
    {
        var deployment = LoadDeployment(project_dir, target);
        var client = NewClient();
        var ws = deployment.GetEffectiveWorkspace(target);
        Dictionary<string, Dictionary<string, object?>>? workspaceItems = null;
        if (!string.IsNullOrEmpty(ws.Name) && client.FindWorkspace(ws.Name) is { } found)
        {
            workspaceItems = client.GetWorkspaceItemsMap(found["id"]!.GetValue<string>());
        }

        var plan = Planner.CreatePlan(deployment, target, workspaceItems);
        var items = plan.Items.Where(i => i.Action != PlanAction.NoChange)
            .Select(i => new Dictionary<string, object?> { ["resource"] = i.ResourceKey, ["type"] = i.ResourceType, ["action"] = i.ActionValue, ["details"] = i.Details })
            .ToList();

        return Format(new Dictionary<string, object?>
        {
            ["workspace"] = ws.Name,
            ["target"] = target ?? "default",
            ["has_changes"] = plan.HasChanges,
            ["summary"] = Summary(items),
            ["items"] = items,
        });
    });

    [McpServerTool(Name = "udp_deploy")]
    [Description("Preview or deploy the deployment. Shows plan first — set confirm: true to execute.")]
    public static string Deploy(
        [Description("Path to project directory")] string? project_dir = null,
        [Description("Target environment")] string? target = null,
        [Description("Preview without making changes")] bool dry_run = false,
        [Description("Set to true to execute after reviewing the plan.")] bool confirm = false) => Guard(() =>
    {
        var deployment = LoadDeployment(project_dir, target);
        var client = NewClient();
        var projDir = ProjectDirOf(project_dir);
        var ws = deployment.GetEffectiveWorkspace(target);
        Dictionary<string, Dictionary<string, object?>>? workspaceItems = null;
        if (!string.IsNullOrEmpty(ws.Name) && client.FindWorkspace(ws.Name) is { } found)
        {
            workspaceItems = client.GetWorkspaceItemsMap(found["id"]!.GetValue<string>());
        }

        var plan = Planner.CreatePlan(deployment, target, workspaceItems);
        if (!plan.HasChanges)
        {
            return "No changes to deploy.";
        }

        var items = plan.Items.Where(i => i.Action != PlanAction.NoChange)
            .Select(i => new Dictionary<string, object?> { ["resource"] = i.ResourceKey, ["type"] = i.ResourceType, ["action"] = i.ActionValue })
            .ToList();
        var planSummary = new Dictionary<string, object?>
        {
            ["workspace"] = ws.Name,
            ["target"] = target ?? "default",
            ["summary"] = Summary(items),
            ["items"] = items,
        };

        if (!confirm && !dry_run)
        {
            planSummary["confirmation_required"] = true;
            planSummary["message"] = "Review the plan above. Call udp_deploy again with confirm: true to execute.";
            return Format(planSummary);
        }
        if (dry_run)
        {
            planSummary["dry_run"] = true;
            return Format(planSummary);
        }

        var deployer = new Deployer(client, deployment, projDir, QuietConsole(), dryRun: false)
        {
            StateManager = new StateManager(projDir, target ?? "default"),
        };
        var result = deployer.Execute(plan, target);
        return Format(new Dictionary<string, object?>
        {
            ["success"] = result.Success,
            ["created"] = result.ItemsCreated,
            ["updated"] = result.ItemsUpdated,
            ["deleted"] = result.ItemsDeleted,
            ["failed"] = result.ItemsFailed,
            ["errors"] = result.Errors,
        });
    });

    [McpServerTool(Name = "udp_destroy")]
    [Description("Preview or destroy all deployment-managed resources. Shows items first — set confirm: true to execute.")]
    public static string Destroy(
        [Description("Path to project directory")] string? project_dir = null,
        [Description("Target environment")] string? target = null,
        [Description("Set to true to execute after reviewing the plan.")] bool confirm = false) => Guard(() =>
    {
        var deployment = LoadDeployment(project_dir, target);
        var client = NewClient();
        var ws = deployment.GetEffectiveWorkspace(target);
        var wsId = !string.IsNullOrEmpty(ws.Name) && client.FindWorkspace(ws.Name) is { } f ? f["id"]!.GetValue<string>() : null;
        if (wsId is null)
        {
            return $"Workspace '{ws.Name}' not found.";
        }

        var items = client.GetWorkspaceItemsMap(wsId);
        var order = Resolver.GetDeploymentOrder(deployment).Select(n => n.Key).ToList();
        order.Reverse();
        var itemsToDelete = order.Where(items.ContainsKey).ToList();

        if (!confirm)
        {
            return Format(new Dictionary<string, object?>
            {
                ["workspace"] = ws.Name,
                ["target"] = target ?? "default",
                ["items_to_destroy"] = itemsToDelete,
                ["count"] = itemsToDelete.Count,
                ["confirmation_required"] = true,
                ["message"] = "Review the items above. Call udp_destroy again with confirm: true to execute.",
            });
        }

        var deleted = new List<string>();
        var errors = new List<string>();
        foreach (var key in itemsToDelete)
        {
            try
            {
                client.DeleteItem(wsId, items[key]["id"]!.ToString()!);
                deleted.Add(key);
            }
            catch (Exception e)
            {
                errors.Add($"{key}: {e.Message}");
            }
        }
        return Format(new Dictionary<string, object?> { ["deleted"] = deleted, ["deleted_count"] = deleted.Count, ["errors"] = errors });
    });

    [McpServerTool(Name = "udp_status")]
    [Description("Show deployed resource health, item IDs, drift detection, and last deploy time.")]
    public static string Status(
        [Description("Path to project directory")] string? project_dir = null,
        [Description("Target environment")] string? target = null) => Guard(() =>
    {
        var deployment = LoadDeployment(project_dir, target);
        var client = NewClient();
        var ws = deployment.GetEffectiveWorkspace(target);
        var wsId = !string.IsNullOrEmpty(ws.Name) && client.FindWorkspace(ws.Name) is { } f ? f["id"]!.GetValue<string>() : null;
        if (wsId is null)
        {
            return $"Workspace '{ws.Name}' not found. Deploy first.";
        }

        var items = client.GetWorkspaceItemsMap(wsId);
        var deploymentKeys = deployment.Resources.AllResourceKeys();
        var state = new StateManager(ProjectDirOf(project_dir), target ?? "default").Load();

        var resources = new List<Dictionary<string, object?>>();
        foreach (var key in deploymentKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var inWorkspace = items.ContainsKey(key);
            resources.Add(new Dictionary<string, object?>
            {
                ["name"] = key,
                ["type"] = deployment.Resources.GetResourceType(key) ?? "",
                ["status"] = inWorkspace ? "deployed" : "pending",
                ["item_id"] = inWorkspace ? Trunc(items[key]["id"]?.ToString()) : null,
            });
        }
        var unmanaged = items.Keys.Where(k => !deploymentKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        foreach (var key in unmanaged)
        {
            resources.Add(new Dictionary<string, object?>
            {
                ["name"] = key,
                ["type"] = items[key].GetValueOrDefault("type")?.ToString() ?? "",
                ["status"] = "unmanaged",
                ["item_id"] = Trunc(items[key].GetValueOrDefault("id")?.ToString()),
            });
        }

        return Format(new Dictionary<string, object?>
        {
            ["workspace"] = ws.Name,
            ["workspace_id"] = wsId,
            ["last_deploy"] = state.LastDeployed,
            ["deployment_items"] = deploymentKeys.Count,
            ["workspace_items"] = items.Count,
            ["resources"] = resources,
            ["drift_count"] = unmanaged.Count,
        });
    });

    [McpServerTool(Name = "udp_drift")]
    [Description("Detect drift between deployed state and live workspace. Shows items added, removed, or modified outside of udpi30.")]
    public static string Drift(
        [Description("Path to project directory")] string? project_dir = null,
        [Description("Target environment")] string? target = null) => Guard(() =>
    {
        var deployment = LoadDeployment(project_dir, target);
        var client = NewClient();
        var ws = deployment.GetEffectiveWorkspace(target);
        var wsId = !string.IsNullOrEmpty(ws.Name) && client.FindWorkspace(ws.Name) is { } f ? f["id"]!.GetValue<string>() : null;
        if (wsId is null)
        {
            return "Workspace not found.";
        }

        var items = client.GetWorkspaceItemsMap(wsId);
        var deploymentKeys = deployment.Resources.AllResourceKeys();
        var added = items.Keys.Where(k => !deploymentKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var removed = deploymentKeys.Where(k => !items.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        return Format(new Dictionary<string, object?>
        {
            ["has_drift"] = added.Count > 0 || removed.Count > 0,
            ["added_in_workspace"] = added,
            ["missing_from_workspace"] = removed,
        });
    });

    [McpServerTool(Name = "udp_run")]
    [Description("Run a notebook or pipeline in the Fabric workspace.")]
    public static string Run(
        [Description("Name of notebook or pipeline to run")] string resource_name,
        [Description("Path to project directory")] string? project_dir = null,
        [Description("Target environment")] string? target = null,
        [Description("Key-value parameters to pass")] Dictionary<string, string>? parameters = null) => Guard(() =>
    {
        var deployment = LoadDeployment(project_dir, target);
        var client = NewClient();
        var ws = deployment.GetEffectiveWorkspace(target);
        var wsId = !string.IsNullOrEmpty(ws.Name) && client.FindWorkspace(ws.Name) is { } f ? f["id"]!.GetValue<string>() : null;
        if (wsId is null)
        {
            return "Workspace not found.";
        }

        var items = client.GetWorkspaceItemsMap(wsId);
        if (!items.TryGetValue(resource_name, out var info))
        {
            return $"Resource '{resource_name}' not found in workspace.";
        }

        var itemType = info.GetValueOrDefault("type")?.ToString() ?? "";
        var jobType = itemType == "Notebook" ? "RunNotebook" : "Pipeline";
        JsonNode? executionData = null;
        if (parameters is { Count: > 0 })
        {
            var paramObj = new JsonObject();
            foreach (var (k, v) in parameters)
            {
                paramObj[k] = new JsonObject { ["value"] = v, ["type"] = "string" };
            }
            executionData = new JsonObject { ["parameters"] = paramObj };
        }

        try
        {
            client.RunItemJob(wsId, info["id"]!.ToString()!, jobType, executionData);
            return Format(new Dictionary<string, object?>
            {
                ["status"] = "submitted",
                ["resource"] = resource_name,
                ["item_id"] = info["id"]!.ToString(),
                ["job_type"] = jobType,
            });
        }
        catch (Exception e)
        {
            return $"Failed to run {resource_name}: {e.Message}";
        }
    });

    [McpServerTool(Name = "udp_history")]
    [Description("Show deployment history for a target environment.")]
    public static string History(
        [Description("Path to project directory")] string? project_dir = null,
        [Description("Target environment")] string? target = null,
        [Description("Max entries to show")] int limit = 10) => Guard(() =>
    {
        var stateMgr = new StateManager(ProjectDirOf(project_dir), target ?? "default");
        var entries = stateMgr.ListHistory(limit);
        return Format(entries.Select(e => e.DeepClone()).ToList());
    });

    [McpServerTool(Name = "udp_diag")]
    [Description("Diagnose common configuration issues: .NET runtime, Azure auth, Fabric API, deployment validity.")]
    public static string Diag(
        [Description("Path to project directory")] string? project_dir = null) => Guard(() =>
    {
        var checks = new List<Dictionary<string, object?>>();
        void Add(string name, bool ok) => checks.Add(new Dictionary<string, object?> { ["check"] = name, ["passed"] = ok });

        Add($".NET {Environment.Version}", Environment.Version.Major >= 9);
        Add("Azure CLI installed", RunAz("--version"));
        Add("Azure CLI authenticated", RunAz("account show -o none"));
        try
        {
            NewClient().ListWorkspaces();
            Add("Fabric API reachable", true);
        }
        catch
        {
            Add("Fabric API reachable", false);
        }

        return Format(new Dictionary<string, object?>
        {
            ["checks"] = checks,
            ["passed"] = checks.Count(c => (bool)c["passed"]!),
            ["failed"] = checks.Count(c => !(bool)c["passed"]!),
        });
    });

    [McpServerTool(Name = "udp_list_templates")]
    [Description("List available project templates (medallion, blank, etc.).")]
    public static string ListTemplates() => Guard(() =>
        Format(TemplateEngine.ListTemplates().Select(t => t.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString())).ToList()));

    [McpServerTool(Name = "udp_list_workspaces")]
    [Description("List Fabric workspaces accessible to the current user.")]
    public static string ListWorkspaces() => Guard(() =>
    {
        var workspaces = NewClient().ListWorkspaces().Select(ws => new Dictionary<string, object?>
        {
            ["name"] = ws["displayName"]?.GetValue<string>() ?? "",
            ["id"] = ws["id"]?.GetValue<string>() ?? "",
            ["type"] = ws["type"]?.GetValue<string>() ?? "",
        }).ToList();
        return Format(workspaces);
    });

    [McpServerTool(Name = "udp_list_capacities")]
    [Description("List available Fabric capacities with their IDs, SKUs, and regions.")]
    public static string ListCapacities() => Guard(() =>
    {
        var output = RunAzCapture("rest --method get --url https://api.fabric.microsoft.com/v1/capacities --resource https://api.fabric.microsoft.com");
        if (output is null)
        {
            return "Error listing capacities: az rest failed";
        }
        var caps = (JsonNode.Parse(output)?["value"] as JsonArray)?.Select(c => new Dictionary<string, object?>
        {
            ["name"] = c?["displayName"]?.GetValue<string>() ?? "",
            ["id"] = c?["id"]?.GetValue<string>() ?? "",
            ["sku"] = c?["sku"]?.GetValue<string>() ?? "",
            ["region"] = c?["region"]?.GetValue<string>() ?? "",
            ["state"] = c?["state"]?.GetValue<string>() ?? "",
        }).ToList() ?? [];
        return Format(caps);
    });

    [McpServerTool(Name = "udp_export")]
    [Description("Export item definitions from a deployed workspace to local files.")]
    public static string Export(
        [Description("Path to project directory")] string? project_dir = null,
        [Description("Target environment")] string? target = null,
        [Description("Output directory for exported files")] string output_dir = ".") => Guard(() =>
    {
        var deployment = LoadDeployment(project_dir, target);
        var client = NewClient();
        var ws = deployment.GetEffectiveWorkspace(target);
        var wsId = !string.IsNullOrEmpty(ws.Name) && client.FindWorkspace(ws.Name) is { } f ? f["id"]!.GetValue<string>() : null;
        if (wsId is null)
        {
            return "Workspace not found.";
        }

        var items = client.GetWorkspaceItemsMap(wsId);
        var exported = new List<string>();
        var errors = new List<string>();
        Directory.CreateDirectory(output_dir);

        foreach (var (name, info) in items)
        {
            try
            {
                var defn = client.GetItemDefinition(wsId, info["id"]!.ToString()!);
                if (defn["definition"]?["parts"] is JsonArray parts && parts.Count > 0)
                {
                    var itemDir = Path.Combine(output_dir, name);
                    Directory.CreateDirectory(itemDir);
                    foreach (var part in parts)
                    {
                        var filePath = Path.Combine(itemDir, (part?["path"]?.GetValue<string>() ?? "content").Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                        File.WriteAllBytes(filePath, Convert.FromBase64String(part?["payload"]?.GetValue<string>() ?? ""));
                    }
                    exported.Add(name);
                }
            }
            catch (Exception e)
            {
                errors.Add($"{name}: {e.Message}");
            }
        }
        return Format(new Dictionary<string, object?> { ["exported"] = exported, ["exported_count"] = exported.Count, ["errors"] = errors });
    });

    [McpServerTool(Name = "udp_generate")]
    [Description("Generate a udp.yml from an existing Fabric workspace, optionally importing deployed Entra and Azure resources.")]
    public static string Generate(
        [Description("Workspace name or ID")] string workspace,
        [Description("Output directory")] string output_dir = ".",
        [Description("Also import deployed Microsoft Entra groups and app registrations")] bool include_entra = false,
        [Description("Also import deployed Azure resources (via the az CLI)")] bool include_azure = false,
        [Description("Azure subscription to scan (blank = az CLI default)")] string? subscription = null,
        [Description("Limit Azure discovery to a single resource group")] string? resource_group = null) => Guard(() =>
    {
        var client = NewClient();
        string wsId;
        if (IsGuid(workspace))
        {
            wsId = workspace;
        }
        else
        {
            var found = client.FindWorkspace(workspace);
            if (found is null)
            {
                return $"Workspace '{workspace}' not found.";
            }
            wsId = found["id"]!.GetValue<string>();
        }

        try
        {
            ReverseGenerator.GenerateDeploymentFromWorkspace(client, workspaceId: wsId, outputDir: output_dir,
                console: QuietConsole(),
                includeEntra: include_entra, includeAzure: include_azure,
                subscription: subscription, resourceGroup: resource_group);
            return Format(new Dictionary<string, object?> { ["status"] = "generated", ["output_dir"] = output_dir });
        }
        catch (Exception e)
        {
            return $"Generate failed: {e.Message}";
        }
    });

    // -- helpers -------------------------------------------------------------

    private static Dictionary<string, object?> Summary(List<Dictionary<string, object?>> items) => new()
    {
        ["create"] = items.Count(i => (string?)i["action"] == "create"),
        ["update"] = items.Count(i => (string?)i["action"] == "update"),
        ["delete"] = items.Count(i => (string?)i["action"] == "delete"),
    };

    private static string? Trunc(string? s) => string.IsNullOrEmpty(s) ? s : s[..Math.Min(12, s.Length)];

    private static bool RunAz(string args) => RunAzCapture(args) is not null;

    private static string? RunAzCapture(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("az", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(15000);
            return proc.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }
}
