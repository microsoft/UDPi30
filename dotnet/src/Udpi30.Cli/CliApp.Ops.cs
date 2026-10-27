using System.CommandLine;
using System.Text;
using System.Text.Json.Nodes;
using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Engine.State;
using Udpi30.Core.Models;
using static Udpi30.Cli.CliHelpers;

namespace Udpi30.Cli;

internal static partial class CliApp
{
    // -- drift ---------------------------------------------------------------

    private static Command DriftCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var cmd = new Command("drift", "Detect drift between deployed state and live workspace.") { file, target };

        cmd.SetAction(pr =>
        {
            DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file), pr.GetValue(target));
            }
            catch (DeploymentLoadError e)
            {
                Error(e.Message);
                return 1;
            }

            var stateMgr = BuildStateManager(deployment, ProjectDir(pr.GetValue(file)), pr.GetValue(target) ?? "default");
            var state = stateMgr.Load();
            if (string.IsNullOrEmpty(state.WorkspaceId))
            {
                Ansi.MarkupLine("[yellow]No deployment state found.[/] Run 'udpi30 deploy' first.");
                return 0;
            }

            var client = NewClient();
            Dictionary<string, Dictionary<string, object?>> liveItems;
            try
            {
                liveItems = client.GetWorkspaceItemsMap(state.WorkspaceId);
            }
            catch (Exception e)
            {
                Ansi.MarkupLine($"[red]Error fetching workspace:[/] {Markup.Escape(e.Message)}");
                return 1;
            }

            var drift = stateMgr.DetectDrift(liveItems);
            if (drift.Count == 0)
            {
                Ansi.MarkupLine("[bold green]No drift detected.[/] Workspace matches deployed state.");
                return 0;
            }

            Ansi.MarkupLine($"[bold yellow]Drift detected:[/] {drift.Count} item(s)");
            Ansi.WriteLine();
            foreach (var (key, driftType) in drift.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var color = driftType switch { "added" => "green", "removed" => "red", "modified" => "yellow", _ => "white" };
                var symbol = driftType switch { "added" => "+", "removed" => "-", "modified" => "~", _ => "?" };
                Ansi.MarkupLine($"  [{color}]{symbol}[/] {Markup.Escape(key)}: {driftType}");
            }
            Ansi.WriteLine();
            Ansi.MarkupLine("  Run 'udpi30 deploy' to reconcile, or 'udpi30 plan' to preview changes.");
            return 0;
        });
        return cmd;
    }

    // -- export --------------------------------------------------------------

    private static Command ExportCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var resource = new Option<string?>("--resource", "-r") { Description = "Export a specific resource (default: all)" };
        var output = new Option<string>("--output", "-o") { Description = "Output directory", DefaultValueFactory = _ => "." };

        var cmd = new Command("export", "Export item definitions from deployed workspace to local files.")
        { file, target, resource, output };

        cmd.SetAction(pr =>
        {
            var outputDir = pr.GetValue(output)!;
            Directory.CreateDirectory(outputDir);

            DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file), pr.GetValue(target));
            }
            catch (DeploymentLoadError e)
            {
                Error(e.Message);
                return 1;
            }

            var client = NewClient();
            var ws = deployment.GetEffectiveWorkspace(pr.GetValue(target));
            var workspaceId = ResolveWorkspaceId(client, ws);
            if (workspaceId is null)
            {
                Error("Workspace not found");
                return 1;
            }

            var existing = client.GetWorkspaceItemsMap(workspaceId);
            var resourceName = pr.GetValue(resource);
            Dictionary<string, Dictionary<string, object?>> itemsToExport;
            if (!string.IsNullOrEmpty(resourceName))
            {
                if (!existing.ContainsKey(resourceName))
                {
                    Error($"'{resourceName}' not found in workspace");
                    return 1;
                }
                itemsToExport = new Dictionary<string, Dictionary<string, object?>> { [resourceName] = existing[resourceName] };
            }
            else
            {
                itemsToExport = existing;
            }

            Ansi.MarkupLine($"Exporting from workspace: {Markup.Escape(ws.Name ?? workspaceId)}");
            Ansi.WriteLine();
            var exported = 0;

            foreach (var (name, info) in itemsToExport.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var itemId = info.GetValueOrDefault("id")?.ToString();
                var itemType = info.GetValueOrDefault("type")?.ToString() ?? "Unknown";
                if (string.IsNullOrEmpty(itemId))
                {
                    continue;
                }
                try
                {
                    var definition = client.GetItemDefinition(workspaceId, itemId);
                    if (definition["definition"]?["parts"] is not JsonArray parts || parts.Count == 0)
                    {
                        Ansi.MarkupLine($"  [dim]=[/] {Markup.Escape(name)} ({Markup.Escape(itemType)}): no exportable definition");
                        continue;
                    }
                    var itemDir = Path.Combine(outputDir, name);
                    Directory.CreateDirectory(itemDir);
                    foreach (var part in parts)
                    {
                        var partPath = part?["path"]?.GetValue<string>() ?? "";
                        var payload = part?["payload"]?.GetValue<string>() ?? "";
                        var payloadType = part?["payloadType"]?.GetValue<string>() ?? "";
                        if (!string.IsNullOrEmpty(payload) && payloadType == "InlineBase64")
                        {
                            var filePath = Path.Combine(itemDir, partPath.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                            File.WriteAllBytes(filePath, Convert.FromBase64String(payload));
                        }
                    }
                    Ansi.MarkupLine($"  [green]+[/] {Markup.Escape(name)} ({Markup.Escape(itemType)}): {parts.Count} files → {Markup.Escape(itemDir)}");
                    exported++;
                }
                catch (Exception e)
                {
                    Ansi.MarkupLine($"  [yellow]![/] {Markup.Escape(name)} ({Markup.Escape(itemType)}): {Markup.Escape(e.Message)}");
                }
            }

            Ansi.WriteLine();
            Ansi.MarkupLine($"Exported {exported} item(s) to {Markup.Escape(Path.GetFullPath(outputDir))}");
            return 0;
        });
        return cmd;
    }

    // -- history -------------------------------------------------------------

    private static Command HistoryCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var limit = new Option<int>("--limit", "-n") { Description = "Number of entries to show", DefaultValueFactory = _ => 20 };

        var cmd = new Command("history", "Show deployment history.") { file, target, limit };

        cmd.SetAction(pr =>
        {
            var stateMgr = new StateManager(ProjectDir(pr.GetValue(file)), pr.GetValue(target) ?? "default");
            var entries = stateMgr.ListHistory(pr.GetValue(limit));
            if (entries.Count == 0)
            {
                Ansi.MarkupLine("[dim]No deployment history found.[/]");
                return 0;
            }

            Ansi.MarkupLine($"[bold]Deployment History ({Markup.Escape(pr.GetValue(target) ?? "default")}):[/]");
            Ansi.WriteLine();
            foreach (var entry in entries)
            {
                var ts = FormatTimestamp(entry["timestamp"]?.GetValue<double>() ?? 0);
                Ansi.MarkupLine(
                    $"  [bold]{Markup.Escape(entry["deploy_id"]?.ToString() ?? "?")}[/]  " +
                    $"{ts}  v{Markup.Escape(entry["deployment_version"]?.ToString() ?? "?")}  " +
                    $"{entry["resource_count"]?.GetValue<int>() ?? 0} resources  " +
                    $"{Markup.Escape(entry["summary"]?.ToString() ?? "")}");
            }
            return 0;
        });
        return cmd;
    }

    // -- rollback ------------------------------------------------------------

    private static Command RollbackCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var to = new Option<string?>("--to") { Description = "Deploy ID to rollback to" };
        var useLast = new Option<bool>("--last") { Description = "Rollback to previous deployment" };
        var autoApprove = new Option<bool>("--auto-approve", "-y") { Description = "Skip confirmation" };

        var cmd = new Command("rollback", "Rollback to a previous deployment.") { file, target, to, useLast, autoApprove };

        cmd.SetAction(pr =>
        {
            var stateMgr = new StateManager(ProjectDir(pr.GetValue(file)), pr.GetValue(target) ?? "default");
            var entries = stateMgr.ListHistory();
            if (entries.Count < 2)
            {
                Ansi.MarkupLine("[yellow]Not enough deployment history to rollback.[/]");
                return 0;
            }

            JsonObject? targetEntry;
            if (pr.GetValue(useLast))
            {
                targetEntry = entries[1];
            }
            else if (pr.GetValue(to) is { } deployId)
            {
                targetEntry = stateMgr.GetHistoryEntry(deployId);
                if (targetEntry is null)
                {
                    Ansi.MarkupLine($"[red]Deploy ID '{Markup.Escape(deployId)}' not found in history.[/]");
                    return 0;
                }
            }
            else
            {
                Ansi.MarkupLine("[red]Specify --to <deploy-id> or --last[/]");
                return 0;
            }

            var ts = FormatTimestamp(targetEntry["timestamp"]?.GetValue<double>() ?? 0);
            Ansi.MarkupLine($"[bold]Rollback target:[/] {Markup.Escape(targetEntry["deploy_id"]?.ToString() ?? "")} ({ts})");
            Ansi.MarkupLine($"  Version: v{Markup.Escape(targetEntry["deployment_version"]?.ToString() ?? "?")}");
            Ansi.MarkupLine($"  Resources: {targetEntry["resource_count"]?.GetValue<int>() ?? 0}");
            Ansi.WriteLine();

            if (!pr.GetValue(autoApprove) && !Confirm("Proceed with rollback?"))
            {
                Ansi.MarkupLine("[dim]Cancelled.[/]");
                return 0;
            }

            var resources = new Dictionary<string, ResourceState>();
            if (targetEntry["resources"] is JsonObject resObj)
            {
                foreach (var (key, info) in resObj)
                {
                    resources[key] = new ResourceState
                    {
                        ItemId = info?["item_id"]?.GetValue<string>() ?? "",
                        ItemType = info?["type"]?.GetValue<string>() ?? "",
                        ResourceKey = key,
                    };
                }
            }

            var state = new DeploymentState
            {
                DeploymentName = targetEntry["deployment_name"]?.GetValue<string>() ?? "",
                DeploymentVersion = targetEntry["deployment_version"]?.GetValue<string>() ?? "",
                TargetName = pr.GetValue(target) ?? "default",
                WorkspaceId = targetEntry["workspace_id"]?.GetValue<string>() ?? "",
                Resources = resources,
                LastDeployed = targetEntry["timestamp"]?.GetValue<double>() ?? 0,
            };
            stateMgr.Save(state);
            Ansi.MarkupLine("[bold green]State rolled back.[/] Run 'udpi30 deploy' to apply.");
            return 0;
        });
        return cmd;
    }

    // -- promote -------------------------------------------------------------

    private static Command PromoteCommand()
    {
        var file = FileOption();
        var from = new Option<string>("--from") { Description = "Source target", Required = true };
        var to = new Option<string>("--to") { Description = "Destination target", Required = true };
        var autoApprove = new Option<bool>("--auto-approve", "-y") { Description = "Skip confirmation" };

        var cmd = new Command("promote", "Promote deployed artifacts from one target to another.") { file, from, to, autoApprove };

        cmd.SetAction(pr =>
        {
            DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file));
            }
            catch (DeploymentLoadError e)
            {
                Error(e.Message);
                return 1;
            }

            var client = NewClient();
            var fromTarget = pr.GetValue(from)!;
            var toTarget = pr.GetValue(to)!;
            var srcWs = deployment.GetEffectiveWorkspace(fromTarget);
            var dstWs = deployment.GetEffectiveWorkspace(toTarget);

            var srcId = ResolveWorkspaceId(client, srcWs);
            var dstId = ResolveWorkspaceId(client, dstWs);

            if (srcId is null)
            {
                Ansi.MarkupLine($"[red]Source workspace '{Markup.Escape(srcWs.Name ?? "")}' not found[/]");
                return 1;
            }

            Ansi.MarkupLine($"[bold]Promote: {Markup.Escape(fromTarget)} → {Markup.Escape(toTarget)}[/]");
            Ansi.MarkupLine($"  Source:  {Markup.Escape(srcWs.Name ?? "")} ({Markup.Escape(srcId)})");
            Ansi.MarkupLine($"  Dest:    {Markup.Escape(dstWs.Name ?? "")} ({Markup.Escape(dstId ?? "will be created")})");
            Ansi.WriteLine();

            var srcItems = client.GetWorkspaceItemsMap(srcId);
            Ansi.MarkupLine($"  {srcItems.Count} items to promote");

            if (!pr.GetValue(autoApprove) && !Confirm("Proceed?"))
            {
                return 0;
            }

            if (dstId is null)
            {
                var result = client.CreateWorkspace(dstWs.Name!, description: dstWs.Description);
                dstId = result["id"]!.GetValue<string>();
                var cap = dstWs.EffectiveCapacityId;
                if (!string.IsNullOrEmpty(cap))
                {
                    client.AssignCapacity(dstId, cap);
                }
                Ansi.MarkupLine($"  Created workspace: {Markup.Escape(dstWs.Name ?? "")}");
            }

            var dstItems = client.GetWorkspaceItemsMap(dstId);
            var promoted = 0;
            foreach (var (name, info) in srcItems)
            {
                try
                {
                    var defn = client.GetItemDefinition(srcId, info["id"]!.ToString()!);
                    var definition = defn["definition"] as JsonObject;

                    if (dstItems.TryGetValue(name, out var dstInfo))
                    {
                        if (definition is not null)
                        {
                            client.UpdateItemDefinition(dstId, dstInfo["id"]!.ToString()!, definition);
                        }
                        Ansi.MarkupLine($"  [yellow]~[/] Updated: {Markup.Escape(name)}");
                    }
                    else
                    {
                        var result = client.CreateItem(dstId, name, info["type"]!.ToString()!, definition: definition);
                        var opUrl = result["operation_url"]?.GetValue<string>();
                        if (opUrl is not null)
                        {
                            client.WaitForOperation(opUrl);
                        }
                        Ansi.MarkupLine($"  [green]+[/] Created: {Markup.Escape(name)}");
                    }
                    promoted++;
                }
                catch (Exception e)
                {
                    Ansi.MarkupLine($"  [red]![/] {Markup.Escape(name)}: {Markup.Escape(e.Message)}");
                }
            }

            Ansi.WriteLine();
            Ansi.MarkupLine($"[bold green]Promoted {promoted} items from {Markup.Escape(fromTarget)} to {Markup.Escape(toTarget)}.[/]");
            return 0;
        });
        return cmd;
    }

    // -- status --------------------------------------------------------------

    private static Command StatusCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var cmd = new Command("status", "Show deployed resource health and status.") { file, target };

        cmd.SetAction(pr =>
        {
            DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file), pr.GetValue(target));
            }
            catch (DeploymentLoadError e)
            {
                Error(e.Message);
                return 1;
            }

            var client = NewClient();
            var ws = deployment.GetEffectiveWorkspace(pr.GetValue(target));
            var workspaceId = ResolveWorkspaceId(client, ws);
            if (workspaceId is null)
            {
                Ansi.MarkupLine("[yellow]Workspace not found.[/] Deploy first.");
                return 0;
            }

            var items = client.GetWorkspaceItemsMap(workspaceId);
            var deploymentKeys = deployment.Resources.AllResourceKeys();
            var stateMgr = new StateManager(ProjectDir(pr.GetValue(file)), pr.GetValue(target) ?? "default");
            var state = stateMgr.Load();

            Ansi.MarkupLine($"[bold]Status: {Markup.Escape(deployment.Deployment.Name)}[/]");
            Ansi.MarkupLine($"  Target:    {Markup.Escape(pr.GetValue(target) ?? "default")}");
            Ansi.MarkupLine($"  Workspace: {Markup.Escape(ws.Name ?? "")} ({Markup.Escape(workspaceId)})");
            if (state.LastDeployed > 0)
            {
                Ansi.MarkupLine($"  Last deploy: {FormatTimestamp(state.LastDeployed)}");
            }
            Ansi.MarkupLine($"  Items in workspace: {items.Count}");
            Ansi.MarkupLine($"  Items in deployment:    {deploymentKeys.Count}");
            Ansi.WriteLine();

            var table = new Table().NoBorder();
            table.AddColumn("Resource");
            table.AddColumn("Type");
            table.AddColumn("Status");
            table.AddColumn("Item ID");

            foreach (var key in deploymentKeys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var inWorkspace = items.ContainsKey(key);
                var inState = state.Resources.ContainsKey(key);
                var rt = deployment.Resources.GetResourceType(key) ?? "";
                string statusStr, itemId;
                if (inWorkspace)
                {
                    statusStr = "[green]deployed[/]";
                    itemId = Trunc(items[key].GetValueOrDefault("id")?.ToString());
                }
                else if (inState)
                {
                    statusStr = "[red]missing[/]";
                    itemId = Trunc(state.Resources[key].ItemId);
                }
                else
                {
                    statusStr = "[yellow]pending[/]";
                    itemId = "";
                }
                table.AddRow(Markup.Escape(key), Markup.Escape(rt), statusStr, Markup.Escape(itemId));
            }

            foreach (var key in items.Keys.Where(k => !deploymentKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
            {
                table.AddRow(Markup.Escape(key), Markup.Escape(items[key].GetValueOrDefault("type")?.ToString() ?? ""),
                    "[dim]unmanaged[/]", Markup.Escape(Trunc(items[key].GetValueOrDefault("id")?.ToString())));
            }

            Ansi.Write(table);

            var drift = stateMgr.DetectDrift(items);
            if (drift.Count > 0)
            {
                Ansi.WriteLine();
                Ansi.MarkupLine($"  [yellow]Drift detected: {drift.Count} item(s)[/]");
            }
            return 0;
        });
        return cmd;
    }

    private static string Trunc(string? s) => string.IsNullOrEmpty(s) ? "" : s[..Math.Min(12, s.Length)];
}
