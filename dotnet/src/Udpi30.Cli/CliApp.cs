using System.CommandLine;
using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Generators;
using static Udpi30.Cli.CliHelpers;

namespace Udpi30.Cli;

/// <summary>
/// Wires up the <c>udpi30</c> command surface on System.CommandLine — a faithful
/// port of the Python <c>click</c> CLI (<c>udp_deployment/cli.py</c>).
/// </summary>
internal static partial class CliApp
{
    private static Option<string?> FileOption() => new("--file", "-f") { Description = "Path to udp.yml" };
    private static Option<string?> TargetOption() => new("--target", "-t") { Description = "Target environment" };

    public static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Project definition for Microsoft Unified Data Platform.");
        root.Subcommands.Add(InitCommand());
        root.Subcommands.Add(ValidateCommand());
        root.Subcommands.Add(PlanCommand());
        root.Subcommands.Add(DeployCommand());
        root.Subcommands.Add(DestroyCommand());
        root.Subcommands.Add(GenerateCommand());
        root.Subcommands.Add(RunCommand());
        root.Subcommands.Add(ListCommand());
        root.Subcommands.Add(BindCommand());
        root.Subcommands.Add(DriftCommand());
        root.Subcommands.Add(ExportCommand());
        root.Subcommands.Add(HistoryCommand());
        root.Subcommands.Add(RollbackCommand());
        root.Subcommands.Add(PromoteCommand());
        root.Subcommands.Add(DiagCommand());
        root.Subcommands.Add(WatchCommand());
        root.Subcommands.Add(StatusCommand());
        root.Subcommands.Add(ImportCommand());
        root.Subcommands.Add(DiffCommand());
        root.Subcommands.Add(GraphCommand());
        root.Subcommands.Add(CheckUpdateCommand());
        root.Subcommands.Add(AdminCommand());
        return root;
    }

    // -- init ----------------------------------------------------------------

    private static Command InitCommand()
    {
        var template = new Option<string?>("--template", "-t") { Description = "Template name or path" };
        var output = new Option<string>("--output", "-o") { Description = "Output directory", DefaultValueFactory = _ => "." };
        var name = new Option<string?>("--name", "-n") { Description = "Deployment project name" };
        var vars = new Option<string[]>("--var") { Description = "Template variables (KEY=VALUE)" };

        var cmd = new Command("init", "Create a new deployment project from a template.")
        { template, output, name, vars };

        cmd.SetAction(pr =>
        {
            var templateName = pr.GetValue(template);
            var outputDir = pr.GetValue(output)!;
            var projectName = pr.GetValue(name);

            if (string.IsNullOrEmpty(templateName))
            {
                templateName = "blank";
            }
            if (string.IsNullOrEmpty(projectName))
            {
                projectName = Ask("Project name");
            }

            var variables = new Dictionary<string, string> { ["project_name"] = projectName };
            foreach (var v in pr.GetValue(vars) ?? [])
            {
                var idx = v.IndexOf('=');
                if (idx >= 0)
                {
                    variables[v[..idx]] = v[(idx + 1)..];
                }
            }

            try
            {
                var dest = outputDir == "." ? Path.Combine(outputDir, projectName) : outputDir;
                TemplateEngine.InitFromTemplate(templateName, dest, variables, Ansi);
                return 0;
            }
            catch (ArgumentException e)
            {
                Error(e.Message);
                return 1;
            }
        });
        return cmd;
    }

    // -- validate ------------------------------------------------------------

    private static Command ValidateCommand()
    {
        var file = FileOption();
        var target = new Option<string?>("--target", "-t") { Description = "Target to validate against" };
        var strict = new Option<bool>("--strict") { Description = "Fail on unresolved variables and warnings" };
        var skipConnectionCheck = new Option<bool>("--skip-connection-check") { Description = "Skip the connectivity check for udp.yml connections" };

        var cmd = new Command("validate", "Validate the deployment definition.") { file, target, strict, skipConnectionCheck };

        cmd.SetAction(pr =>
        {
            Core.Models.DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file), pr.GetValue(target), pr.GetValue(strict));
            }
            catch (DeploymentLoadError e)
            {
                Ansi.MarkupLine($"[red]Validation failed:[/] {Markup.Escape(e.Message)}");
                return 1;
            }

            List<ResourceNode> order;
            try
            {
                order = Resolver.GetDeploymentOrder(deployment);
            }
            catch (DependencyResolutionError e)
            {
                Ansi.MarkupLine($"[red]Dependency error:[/] {Markup.Escape(e.Message)}");
                return 1;
            }

            var typesSummary = new Dictionary<string, int>();
            foreach (var node in order)
            {
                typesSummary[node.ResourceType] = typesSummary.GetValueOrDefault(node.ResourceType) + 1;
            }

            Ansi.MarkupLine("[bold green]Deployment is valid.[/]");
            Ansi.WriteLine();
            Ansi.MarkupLine($"  Deployment:    {Markup.Escape(deployment.Deployment.Name)} v{Markup.Escape(deployment.Deployment.Version)}");
            if (!string.IsNullOrEmpty(deployment.Deployment.Description))
            {
                Ansi.MarkupLine($"  Desc:      {Markup.Escape(deployment.Deployment.Description)}");
            }
            Ansi.MarkupLine($"  Resources: {order.Count}");
            foreach (var (rtype, count) in typesSummary.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                Ansi.MarkupLine($"    {Markup.Escape(rtype)}: {count}");
            }
            var targets = deployment.Targets.Count > 0 ? string.Join(", ", deployment.Targets.Keys) : "(none)";
            Ansi.MarkupLine($"  Targets:   {Markup.Escape(targets)}");

            var targetVal = pr.GetValue(target);
            if (!string.IsNullOrEmpty(targetVal))
            {
                var ws = deployment.GetEffectiveWorkspace(targetVal);
                Ansi.MarkupLine($"  Workspace: {Markup.Escape(ws.Name ?? ws.WorkspaceId ?? "(not set)")}");
                var variables = deployment.ResolveVariables(targetVal);
                if (variables.Count > 0)
                {
                    Ansi.MarkupLine($"  Variables: {variables.Count}");
                }
            }

            Ansi.WriteLine();
            Ansi.MarkupLine("  Deployment order:");
            var i = 1;
            foreach (var node in order)
            {
                var deps = node.DependsOn.Count > 0 ? $" (depends: {string.Join(", ", node.DependsOn)})" : "";
                Ansi.MarkupLine($"    {i}. [[{Markup.Escape(node.ResourceType)}]] {Markup.Escape(node.Key)}{Markup.Escape(deps)}");
                i++;
            }

            var strictVal = pr.GetValue(strict);
            if (!pr.GetValue(skipConnectionCheck) && deployment.Connections.Count > 0)
            {
                Ansi.WriteLine();
                Ansi.MarkupLine("  Connection checks:");
                var unreachable = 0;
                foreach (var result in ConnectionChecker.CheckAll(deployment))
                {
                    if (!result.Tested)
                    {
                        Ansi.MarkupLine($"    [dim]·[/] {Markup.Escape(result.Name)} — skipped ({Markup.Escape(result.Detail ?? "")})");
                    }
                    else if (result.Reachable)
                    {
                        Ansi.MarkupLine($"    [green]✓[/] {Markup.Escape(result.Name)} — reachable ({Markup.Escape(result.Target ?? "")})");
                    }
                    else
                    {
                        unreachable++;
                        Ansi.MarkupLine($"    [yellow]✗[/] {Markup.Escape(result.Name)} — unreachable ({Markup.Escape(result.Target ?? "")}): {Markup.Escape(result.Detail ?? "")}");
                    }
                }
                if (unreachable > 0)
                {
                    Ansi.WriteLine();
                    if (strictVal)
                    {
                        Ansi.MarkupLine($"[red]{unreachable} connection(s) unreachable.[/] (failing because --strict)");
                        return 1;
                    }
                    Ansi.MarkupLine($"[yellow]Warning:[/] {unreachable} connection(s) unreachable. Use --strict to fail on this.");
                }
            }

            return 0;
        });
        return cmd;
    }

    // -- plan ----------------------------------------------------------------

    private static Command PlanCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var autoDelete = new Option<bool>("--auto-delete") { Description = "Plan deletion of unmanaged items" };

        var cmd = new Command("plan", "Preview what changes would be made (dry-run).") { file, target, autoDelete };

        cmd.SetAction(pr =>
        {
            Core.Models.DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file), pr.GetValue(target));
            }
            catch (DeploymentLoadError e)
            {
                Error(e.Message);
                return 1;
            }

            var targetVal = pr.GetValue(target);
            var wsConfig = deployment.GetEffectiveWorkspace(targetVal);
            Dictionary<string, Dictionary<string, object?>>? workspaceItems = null;

            if (!string.IsNullOrEmpty(wsConfig.WorkspaceId) || !string.IsNullOrEmpty(wsConfig.Name))
            {
                try
                {
                    var client = NewClient();
                    var wsId = ResolveWorkspaceId(client, wsConfig);
                    if (wsId is not null)
                    {
                        workspaceItems = client.GetWorkspaceItemsMap(wsId);
                    }
                }
                catch (Exception e)
                {
                    Ansi.MarkupLine($"[yellow]Warning:[/] Could not connect to workspace: {Markup.Escape(e.Message)}");
                    Ansi.MarkupLine("  Planning against empty workspace (all items will be CREATE)");
                    Ansi.WriteLine();
                }
            }

            var plan = Planner.CreatePlan(deployment, targetVal, workspaceItems, pr.GetValue(autoDelete));
            plan.Display(Ansi);
            return 0;
        });
        return cmd;
    }

    // -- deploy --------------------------------------------------------------

    private static Command DeployCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var dryRun = new Option<bool>("--dry-run") { Description = "Preview without deploying" };
        var autoApprove = new Option<bool>("--auto-approve", "-y") { Description = "Skip confirmation" };
        var autoDelete = new Option<bool>("--auto-delete") { Description = "Delete unmanaged items" };
        var force = new Option<bool>("--force") { Description = "Override deployment lock and skip cache" };
        var continueOnError = new Option<bool>("--continue-on-error") { Description = "Keep successfully created items on failure instead of rolling back" };

        var cmd = new Command("deploy", "Deploy the deployment to a target workspace.")
        { file, target, dryRun, autoApprove, autoDelete, force, continueOnError };

        cmd.SetAction(pr =>
        {
            Core.Models.DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file), pr.GetValue(target), strict: true);
            }
            catch (DeploymentLoadError e)
            {
                Error(e.Message);
                return 1;
            }

            var targetVal = pr.GetValue(target);
            var projectDir = ProjectDir(pr.GetValue(file));
            var stateMgr = BuildStateManager(deployment, projectDir, targetVal ?? "default");
            var client = NewClient();

            var wsConfig = deployment.GetEffectiveWorkspace(targetVal);
            Dictionary<string, Dictionary<string, object?>>? workspaceItems = null;
            try
            {
                var wsId = ResolveWorkspaceId(client, wsConfig);
                if (wsId is not null)
                {
                    workspaceItems = client.GetWorkspaceItemsMap(wsId);
                }
            }
            catch
            {
                // Treat as empty workspace.
            }

            var plan = Planner.CreatePlan(deployment, targetVal, workspaceItems, pr.GetValue(autoDelete));
            plan.Display(Ansi);

            if (!plan.HasChanges)
            {
                return 0;
            }

            var dry = pr.GetValue(dryRun);
            if (!dry && !pr.GetValue(autoApprove) && !Confirm("Do you want to apply these changes?"))
            {
                Ansi.MarkupLine("[dim]Cancelled.[/]");
                return 0;
            }

            var deployer = new Deployer(client, deployment, projectDir, Ansi, dryRun: dry)
            {
                StateManager = stateMgr,
                ContinueOnError = pr.GetValue(continueOnError),
            };
            var result = deployer.Execute(plan, targetVal, force: pr.GetValue(force));
            return result.Success ? 0 : 1;
        });
        return cmd;
    }

    // -- destroy -------------------------------------------------------------

    private static Command DestroyCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var autoApprove = new Option<bool>("--auto-approve", "-y") { Description = "Skip confirmation" };
        var deleteWorkspace = new Option<bool>("--delete-workspace") { Description = "Also delete the workspace itself" };

        var cmd = new Command("destroy", "Destroy all deployment-managed resources in the target workspace.")
        { file, target, autoApprove, deleteWorkspace };

        cmd.SetAction(pr =>
        {
            Core.Models.DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file), pr.GetValue(target));
            }
            catch (DeploymentLoadError e)
            {
                Error(e.Message);
                return 1;
            }

            var targetVal = pr.GetValue(target);
            var ws = deployment.GetEffectiveWorkspace(targetVal);
            Ansi.MarkupLine("[bold red]WARNING:[/] This will delete all deployment-managed resources in:");
            Ansi.MarkupLine($"  Workspace: {Markup.Escape(ws.Name ?? ws.WorkspaceId ?? "")}");
            Ansi.MarkupLine($"  Target:    {Markup.Escape(targetVal ?? "(default)")}");
            Ansi.WriteLine();

            var reversed = Enumerable.Reverse(Resolver.GetDeploymentOrder(deployment)).ToList();
            Ansi.MarkupLine("  Resources to destroy (reverse dependency order):");
            var n = 1;
            foreach (var node in reversed)
            {
                Ansi.MarkupLine($"    {n}. [red]-[/] [[{Markup.Escape(node.ResourceType)}]] {Markup.Escape(node.Key)}");
                n++;
            }
            Ansi.WriteLine();

            if (!pr.GetValue(autoApprove))
            {
                var confirmText = Ask($"Type the deployment name '{deployment.Deployment.Name}' to confirm destruction");
                if (confirmText != deployment.Deployment.Name)
                {
                    Ansi.MarkupLine("[dim]Cancelled — name did not match.[/]");
                    return 0;
                }
            }

            var client = NewClient();
            var workspaceId = ResolveWorkspaceId(client, ws);
            if (workspaceId is null)
            {
                Ansi.MarkupLine("[yellow]Workspace not found — nothing to destroy.[/]");
                return 0;
            }

            var existing = client.GetWorkspaceItemsMap(workspaceId);
            var destroyed = 0;
            var failed = 0;
            foreach (var node in reversed)
            {
                if (existing.TryGetValue(node.Key, out var info))
                {
                    var itemId = info.GetValueOrDefault("id")?.ToString();
                    if (!string.IsNullOrEmpty(itemId))
                    {
                        try
                        {
                            client.DeleteItem(workspaceId, itemId);
                            Ansi.MarkupLine($"  [red]-[/] Deleted: {Markup.Escape(node.Key)}");
                            destroyed++;
                        }
                        catch (Exception e)
                        {
                            Ansi.MarkupLine($"  [red]ERROR[/] {Markup.Escape(node.Key)}: {Markup.Escape(e.Message)}");
                            failed++;
                        }
                    }
                }
                else
                {
                    Ansi.MarkupLine($"  [dim]=[/] Not found: {Markup.Escape(node.Key)}");
                }
            }

            Ansi.WriteLine();
            if (failed > 0)
            {
                Ansi.MarkupLine($"[bold red]Destroy completed with errors.[/] Deleted: {destroyed}, Failed: {failed}");
            }
            else
            {
                Ansi.MarkupLine($"[bold green]Destroy complete.[/] Deleted: {destroyed} resources.");
            }

            if (pr.GetValue(deleteWorkspace) && failed == 0)
            {
                if (pr.GetValue(autoApprove) || Confirm($"Also delete workspace '{ws.Name}'?"))
                {
                    try
                    {
                        client.DeleteWorkspace(workspaceId);
                        Ansi.MarkupLine($"  [red]Workspace deleted: {Markup.Escape(ws.Name ?? "")}[/]");
                    }
                    catch (Exception e)
                    {
                        Ansi.MarkupLine($"  [red]Failed to delete workspace:[/] {Markup.Escape(e.Message)}");
                    }
                }
            }

            var stateMgr = BuildStateManager(deployment, ProjectDir(pr.GetValue(file)), targetVal ?? "default");
            foreach (var node in reversed)
            {
                if (existing.ContainsKey(node.Key))
                {
                    stateMgr.RemoveResource(node.Key);
                }
            }
            return 0;
        });
        return cmd;
    }

    // -- generate ------------------------------------------------------------

    private static Command GenerateCommand()
    {
        var workspace = new Option<string>("--workspace", "-w") { Description = "Workspace name or ID to scan", Required = true };
        var output = new Option<string>("--output", "-o") { Description = "Output directory", DefaultValueFactory = _ => "." };
        var includeEntra = new Option<bool>("--include-entra") { Description = "Also import deployed Microsoft Entra groups and app registrations" };
        var includeAzure = new Option<bool>("--include-azure") { Description = "Also import deployed Azure resources (via the az CLI)" };
        var subscription = new Option<string>("--subscription") { Description = "Azure subscription to scan (defaults to the az CLI's active one)" };
        var resourceGroup = new Option<string>("--resource-group", "-g") { Description = "Limit Azure discovery to a single resource group" };
        var location = new Option<string>("--location") { Description = "Default Azure region to record in the azure: block" };

        var cmd = new Command("generate", "Generate a udp.yml from an existing workspace (optionally importing Entra and Azure).")
        {
            workspace, output, includeEntra, includeAzure, subscription, resourceGroup, location,
        };

        cmd.SetAction(pr =>
        {
            var client = NewClient();
            var ws = pr.GetValue(workspace)!;
            try
            {
                var isGuid = IsGuid(ws);
                ReverseGenerator.GenerateDeploymentFromWorkspace(client,
                    workspaceName: isGuid ? null : ws,
                    workspaceId: isGuid ? ws : null,
                    outputDir: pr.GetValue(output),
                    console: Ansi,
                    includeEntra: pr.GetValue(includeEntra),
                    includeAzure: pr.GetValue(includeAzure),
                    subscription: pr.GetValue(subscription),
                    resourceGroup: pr.GetValue(resourceGroup),
                    azureLocation: pr.GetValue(location));
                return 0;
            }
            catch (Exception e)
            {
                Error(e.Message);
                return 1;
            }
        });
        return cmd;
    }

    // -- run -----------------------------------------------------------------

    private static Command RunCommand()
    {
        var resourceName = new Argument<string>("resource_name");
        var file = FileOption();
        var target = TargetOption();
        var param = new Option<string[]>("--param", "-p") { Description = "Parameters (KEY=VALUE)" };

        var cmd = new Command("run", "Run a specific resource (pipeline or notebook).") { resourceName, file, target, param };

        cmd.SetAction(pr =>
        {
            Core.Models.DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file), pr.GetValue(target));
            }
            catch (DeploymentLoadError e)
            {
                Error(e.Message);
                return 1;
            }

            var name = pr.GetValue(resourceName)!;
            var resourceType = deployment.Resources.GetResourceType(name);
            if (resourceType is null)
            {
                Error($"Resource '{name}' not found in deployment");
                return 1;
            }
            if (resourceType is not ("notebooks" or "pipelines"))
            {
                Error($"Cannot run resource type '{resourceType}'. Only notebooks and pipelines are runnable.");
                return 1;
            }

            var client = NewClient();
            var ws = deployment.GetEffectiveWorkspace(pr.GetValue(target));
            var workspaceId = ResolveWorkspaceId(client, ws);
            if (workspaceId is null)
            {
                Error($"Workspace '{ws.Name}' not found");
                return 1;
            }

            var existing = client.GetWorkspaceItemsMap(workspaceId);
            if (!existing.TryGetValue(name, out var info))
            {
                Error($"'{name}' not found in workspace. Deploy first with 'udpi30 deploy'.");
                return 1;
            }

            var itemId = info["id"]!.ToString()!;
            var itemType = info.GetValueOrDefault("type")?.ToString();

            Ansi.MarkupLine($"Running [[{Markup.Escape(resourceType[..^1])}]]: [bold]{Markup.Escape(name)}[/]");
            Ansi.MarkupLine($"  Workspace: {Markup.Escape(ws.Name ?? "")} ({Markup.Escape(workspaceId)})");
            Ansi.MarkupLine($"  Item ID:   {Markup.Escape(itemId)}");
            Ansi.WriteLine();

            var parameters = new Dictionary<string, object?>();
            if (deployment.Resources.Notebooks.TryGetValue(name, out var nb))
            {
                foreach (var (k, v) in nb.Parameters)
                {
                    parameters[k] = v;
                }
            }
            foreach (var p in pr.GetValue(param) ?? [])
            {
                var idx = p.IndexOf('=');
                if (idx >= 0)
                {
                    parameters[p[..idx]] = p[(idx + 1)..];
                }
            }

            System.Text.Json.Nodes.JsonNode? executionData = null;
            if (parameters.Count > 0)
            {
                var paramObj = new System.Text.Json.Nodes.JsonObject();
                foreach (var (k, v) in parameters)
                {
                    paramObj[k] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["value"] = System.Text.Json.Nodes.JsonValue.Create(v?.ToString()),
                        ["type"] = "string",
                    };
                }
                executionData = new System.Text.Json.Nodes.JsonObject { ["parameters"] = paramObj };
                Ansi.MarkupLine($"  Parameters: {Markup.Escape(string.Join(", ", parameters.Select(kv => $"{kv.Key}={kv.Value}")))}");
            }

            try
            {
                var jobType = itemType == "Notebook" ? "RunNotebook" : "Pipeline";
                var result = client.RunItemJob(workspaceId, itemId, jobType, executionData);
                var opUrl = result?["operation_url"]?.GetValue<string>();
                if (opUrl is not null)
                {
                    Ansi.MarkupLine("[dim]Job submitted. Waiting for completion...[/]");
                    try
                    {
                        client.WaitForOperation(opUrl, timeout: 600);
                        Ansi.MarkupLine("[bold green]Run complete.[/]");
                    }
                    catch (Exception e)
                    {
                        Ansi.MarkupLine($"[yellow]Job submitted but could not track completion:[/] {Markup.Escape(e.Message)}");
                        Ansi.MarkupLine("  Check the Fabric portal for run status.");
                    }
                }
                else
                {
                    Ansi.MarkupLine("[bold green]Run triggered successfully.[/]");
                }
                return 0;
            }
            catch (Exception e)
            {
                Ansi.MarkupLine($"[red]Error triggering run:[/] {Markup.Escape(e.Message)}");
                return 1;
            }
        });
        return cmd;
    }

    // -- list ----------------------------------------------------------------

    private static Command ListCommand()
    {
        var cmd = new Command("list", "List available deployment templates.");
        cmd.SetAction(_ =>
        {
            var templates = TemplateEngine.ListTemplates();
            if (templates.Count == 0)
            {
                Ansi.MarkupLine("[dim]No templates found.[/]");
                return 0;
            }
            Ansi.MarkupLine("[bold]Available templates:[/]");
            Ansi.WriteLine();
            foreach (var tmpl in templates)
            {
                Ansi.MarkupLine($"  [bold]{Markup.Escape(tmpl.GetValueOrDefault("name")?.ToString() ?? "unknown")}[/]");
                if (tmpl.GetValueOrDefault("description") is { } d)
                {
                    Ansi.MarkupLine($"    {Markup.Escape(d.ToString() ?? "")}");
                }
            }
            Ansi.WriteLine();
            Ansi.MarkupLine("Usage: udpi30 init --template <name> --name <project-name>");
            return 0;
        });
        return cmd;
    }

    // -- bind ----------------------------------------------------------------

    private static Command BindCommand()
    {
        var resourceName = new Argument<string>("resource_name");
        var workspace = new Option<string>("--workspace", "-w") { Description = "Workspace name or ID", Required = true };
        var file = FileOption();

        var cmd = new Command("bind", "Bind an existing workspace resource to deployment management.")
        { resourceName, workspace, file };

        cmd.SetAction(pr =>
        {
            Core.Models.DeploymentDefinition deployment;
            try
            {
                deployment = Loader.LoadDeployment(pr.GetValue(file));
            }
            catch (DeploymentLoadError e)
            {
                Error(e.Message);
                return 1;
            }

            var name = pr.GetValue(resourceName)!;
            var resourceType = deployment.Resources.GetResourceType(name);
            if (resourceType is null)
            {
                Error($"Resource '{name}' not found in udp.yml");
                Ansi.MarkupLine("  Add the resource definition to udp.yml first, then bind it.");
                return 1;
            }

            var client = NewClient();
            var wsArg = pr.GetValue(workspace)!;
            string workspaceId;
            if (IsGuid(wsArg))
            {
                workspaceId = wsArg;
            }
            else
            {
                var found = client.FindWorkspace(wsArg);
                if (found is null)
                {
                    Error($"Workspace '{wsArg}' not found");
                    return 1;
                }
                workspaceId = found["id"]!.GetValue<string>();
            }

            var existing = client.GetWorkspaceItemsMap(workspaceId);
            if (!existing.TryGetValue(name, out var itemInfo))
            {
                Error($"'{name}' not found in workspace");
                return 1;
            }

            Ansi.MarkupLine($"[bold green]Bound:[/] {Markup.Escape(name)}");
            Ansi.MarkupLine($"  Type:      {Markup.Escape(itemInfo.GetValueOrDefault("type")?.ToString() ?? "")}");
            Ansi.MarkupLine($"  Item ID:   {Markup.Escape(itemInfo.GetValueOrDefault("id")?.ToString() ?? "")}");
            Ansi.MarkupLine($"  Workspace: {Markup.Escape(wsArg)}");

            var stateMgr = BuildStateManager(deployment, ProjectDir(pr.GetValue(file)), "default");
            stateMgr.RecordDeployment("bound", "", workspaceId, wsArg,
                new Dictionary<string, Dictionary<string, object?>>
                {
                    [name] = new() { ["id"] = itemInfo.GetValueOrDefault("id"), ["type"] = itemInfo.GetValueOrDefault("type") },
                });
            Ansi.MarkupLine("  Recorded to state. Visible in 'udpi30 status'.");
            return 0;
        });
        return cmd;
    }
}
