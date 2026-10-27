using System.CommandLine;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Engine.State;
using Udpi30.Core.Models;
using static Udpi30.Cli.CliHelpers;

namespace Udpi30.Cli;

internal static partial class CliApp
{
    // -- diag ----------------------------------------------------------------

    private static Command DiagCommand()
    {
        var cmd = new Command("diag", "Diagnose common configuration issues.");
        cmd.SetAction(_ =>
        {
            var passed = 0;
            var failed = 0;
            void Check(string name, Func<bool> fn)
            {
                try
                {
                    if (fn())
                    {
                        Ansi.MarkupLine($"  [green]✓[/] {Markup.Escape(name)}");
                        passed++;
                    }
                    else
                    {
                        Ansi.MarkupLine($"  [red]✗[/] {Markup.Escape(name)}");
                        failed++;
                    }
                }
                catch (Exception e)
                {
                    Ansi.MarkupLine($"  [red]✗[/] {Markup.Escape(name)}: {Markup.Escape(e.Message)}");
                    failed++;
                }
            }

            Ansi.MarkupLine("[bold]udpi30 diag[/]");
            Ansi.WriteLine();

            Check($".NET runtime {Environment.Version}", () => Environment.Version.Major >= 9);
            Check("Azure CLI installed", () => RunProcess("az", "--version"));
            Check("Azure CLI authenticated", () => RunProcess("az", "account show --query name -o tsv"));
            Check("Fabric API reachable", () => NewClient().ListWorkspaces() is not null);
            Check("udp.yml found", () => File.Exists("udp.yml") || File.Exists("udp.yaml"));
            if (File.Exists("udp.yml") || File.Exists("udp.yaml"))
            {
                Core.Models.DeploymentDefinition? loaded = null;
                Check("Deployment validates", () => { loaded = Loader.LoadDeployment(); return true; });

                if (loaded is { Connections.Count: > 0 })
                {
                    foreach (var result in ConnectionChecker.CheckAll(loaded))
                    {
                        if (!result.Tested)
                        {
                            Ansi.MarkupLine($"  [dim]·[/] Connection '{Markup.Escape(result.Name)}' [dim]skipped ({Markup.Escape(result.Detail ?? "")})[/]");
                            continue;
                        }
                        Check($"Connection '{result.Name}' reachable ({result.Target})",
                            () => result.Reachable ? true : throw new Exception(result.Detail ?? "unreachable"));
                    }
                }
            }

            Ansi.WriteLine();
            Ansi.MarkupLine($"  {passed} passed, {failed} failed");
            return 0;
        });
        return cmd;
    }

    private static bool RunProcess(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }
            proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // -- watch ---------------------------------------------------------------

    private static Command WatchCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var interval = new Option<int>("--interval") { Description = "Check interval in seconds", DefaultValueFactory = _ => 5 };

        var cmd = new Command("watch", "Watch for file changes and auto-deploy to target.") { file, target, interval };

        cmd.SetAction(pr =>
        {
            var deploymentFile = pr.GetValue(file);
            var targetVal = pr.GetValue(target);
            var projectDir = ProjectDir(deploymentFile);
            var seconds = pr.GetValue(interval);

            Ansi.MarkupLine($"[bold]Watching for changes...[/] (target: {Markup.Escape(targetVal ?? "default")}, interval: {seconds}s)");
            Ansi.MarkupLine("  Press Ctrl+C to stop.");
            Ansi.WriteLine();

            var stop = false;
            System.Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };

            var prevHashes = FileHashes(projectDir);
            while (!stop)
            {
                Thread.Sleep(seconds * 1000);
                if (stop)
                {
                    break;
                }
                var currHashes = FileHashes(projectDir);
                var changed = currHashes.Where(kv => !prevHashes.TryGetValue(kv.Key, out var h) || h != kv.Value)
                    .Select(kv => kv.Key).ToList();

                if (changed.Count > 0)
                {
                    Ansi.MarkupLine($"  [[{DateTime.Now:HH:mm:ss}]] Changed: {Markup.Escape(string.Join(", ", changed.Take(5)))}");
                    try
                    {
                        var deployment = Loader.LoadDeployment(deploymentFile, targetVal);
                        var client = NewClient();
                        var wsId = ResolveWorkspaceId(client, deployment.GetEffectiveWorkspace(targetVal));
                        if (wsId is not null)
                        {
                            var items = client.GetWorkspaceItemsMap(wsId);
                            var plan = Planner.CreatePlan(deployment, targetVal, items);
                            if (plan.HasChanges)
                            {
                                var deployer = new Deployer(client, deployment, projectDir, Ansi);
                                var result = deployer.Execute(plan, targetVal);
                                if (result.Success)
                                {
                                    Ansi.MarkupLine("  [green]Deployed.[/]");
                                }
                            }
                            else
                            {
                                Ansi.MarkupLine("  [dim]No deployment changes.[/]");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Ansi.MarkupLine($"  [red]Deploy failed:[/] {Markup.Escape(e.Message)}");
                    }
                    prevHashes = currHashes;
                }
            }
            Ansi.MarkupLine("\n[dim]Watch stopped.[/]");
            return 0;
        });
        return cmd;
    }

    private static Dictionary<string, string> FileHashes(string directory)
    {
        var hashes = new Dictionary<string, string>();
        var exts = new[] { "*.py", "*.sql", "*.yml", "*.yaml", "*.json", "*.ipynb", "*.tmdl", "*.r", "*.scala" };
        foreach (var ext in exts)
        {
            foreach (var f in Directory.EnumerateFiles(directory, ext, SearchOption.AllDirectories))
            {
                if (f.Contains(".udpi30") || f.Contains("__pycache__") || f.Contains(".venv"))
                {
                    continue;
                }
                try
                {
                    var hash = Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(f)));
                    hashes[Path.GetRelativePath(directory, f)] = hash;
                }
                catch
                {
                    // Skip unreadable file.
                }
            }
        }
        return hashes;
    }

    // -- import --------------------------------------------------------------

    private static Command ImportCommand()
    {
        var fromTerraform = new Option<string?>("--from-terraform") { Description = "Path to terraform.tfstate" };
        var workspace = new Option<string?>("--workspace", "-w") { Description = "Workspace name or ID to import from" };
        var output = new Option<string>("--output", "-o") { Description = "Output directory", DefaultValueFactory = _ => "." };
        var target = new Option<string>("--target", "-t") { Description = "Target name for state", DefaultValueFactory = _ => "dev" };

        var cmd = new Command("import", "Import existing resources into udpi30 management.")
        { fromTerraform, workspace, output, target };

        cmd.SetAction(pr =>
        {
            var outputDir = pr.GetValue(output)!;
            var targetVal = pr.GetValue(target)!;
            var tfPath = pr.GetValue(fromTerraform);
            var workspaceVal = pr.GetValue(workspace);

            if (!string.IsNullOrEmpty(tfPath))
            {
                if (!File.Exists(tfPath))
                {
                    Ansi.MarkupLine($"[red]File not found: {Markup.Escape(tfPath)}[/]");
                    return 1;
                }
                var tfState = JsonNode.Parse(File.ReadAllText(tfPath)) as JsonObject;
                var udpResources = new Dictionary<string, Dictionary<string, object?>>();
                if (tfState?["resources"] is JsonArray resources)
                {
                    foreach (var res in resources)
                    {
                        var type = res?["type"]?.GetValue<string>() ?? "";
                        if (!type.Contains("microsoft_udp"))
                        {
                            continue;
                        }
                        if (res?["instances"] is JsonArray instances)
                        {
                            foreach (var inst in instances)
                            {
                                var attrs = inst?["attributes"] as JsonObject;
                                var name = attrs?["display_name"]?.GetValue<string>() ?? res?["name"]?.GetValue<string>() ?? "unknown";
                                udpResources[name] = new Dictionary<string, object?>
                                {
                                    ["type"] = type.Replace("microsoft_udp_", ""),
                                    ["id"] = attrs?["id"]?.GetValue<string>() ?? "",
                                    ["workspace_id"] = attrs?["workspace_id"]?.GetValue<string>() ?? "",
                                };
                            }
                        }
                    }
                }

                Ansi.MarkupLine($"Found {udpResources.Count} Fabric resources in Terraform state");
                foreach (var (name, info) in udpResources.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    Ansi.MarkupLine($"  {info["type"],-20} {Markup.Escape(name)}");
                }

                if (udpResources.Count > 0)
                {
                    var stateMgr = new StateManager(outputDir, targetVal);
                    var wsId = udpResources.Values.FirstOrDefault()?.GetValueOrDefault("workspace_id")?.ToString() ?? "";
                    var deployed = udpResources.ToDictionary(kv => kv.Key,
                        kv => new Dictionary<string, object?> { ["id"] = kv.Value["id"], ["type"] = kv.Value["type"] });
                    stateMgr.RecordDeployment("imported", "0.0.0", wsId, "", deployed);
                    Ansi.MarkupLine($"\n[green]Imported {udpResources.Count} resources to udpi30 state.[/]");
                }
                return 0;
            }

            if (!string.IsNullOrEmpty(workspaceVal))
            {
                var client = NewClient();
                string wsId, wsName;
                if (IsGuid(workspaceVal))
                {
                    wsId = workspaceVal;
                    wsName = workspaceVal;
                }
                else
                {
                    var found = client.FindWorkspace(workspaceVal);
                    if (found is null)
                    {
                        Ansi.MarkupLine($"[red]Workspace '{Markup.Escape(workspaceVal)}' not found[/]");
                        return 1;
                    }
                    wsId = found["id"]!.GetValue<string>();
                    wsName = found["displayName"]?.GetValue<string>() ?? workspaceVal;
                }

                var items = client.GetWorkspaceItemsMap(wsId);
                Ansi.MarkupLine($"Found {items.Count} items in workspace '{Markup.Escape(wsName)}'");

                var stateMgr = new StateManager(outputDir, targetVal);
                var deployed = items.ToDictionary(kv => kv.Key,
                    kv => new Dictionary<string, object?> { ["id"] = kv.Value.GetValueOrDefault("id"), ["type"] = kv.Value.GetValueOrDefault("type") });
                stateMgr.RecordDeployment("imported", "0.0.0", wsId, wsName, deployed);
                Ansi.MarkupLine($"[green]Imported {items.Count} resources to udpi30 state.[/]");
                return 0;
            }

            Ansi.MarkupLine("[red]Specify --from-terraform or --workspace[/]");
            return 1;
        });
        return cmd;
    }

    // -- diff ----------------------------------------------------------------

    private static Command DiffCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var resourceName = new Argument<string?>("resource_name") { Arity = ArgumentArity.ZeroOrOne };

        var cmd = new Command("diff", "Show definition-level diff between local and deployed.") { file, target, resourceName };

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
                Ansi.MarkupLine("[red]Workspace not found[/]");
                return 1;
            }

            var existing = client.GetWorkspaceItemsMap(workspaceId);
            var deployer = new Deployer(client, deployment, ProjectDir(pr.GetValue(file)), Ansi, dryRun: true);

            var resources = new Dictionary<string, string>();
            var single = pr.GetValue(resourceName);
            if (!string.IsNullOrEmpty(single))
            {
                if (deployment.Resources.GetResourceType(single) is { } rt)
                {
                    resources[single] = rt;
                }
            }
            else
            {
                foreach (var key in deployment.Resources.AllResourceKeys())
                {
                    if (deployment.Resources.GetResourceType(key) is { } rt)
                    {
                        resources[key] = rt;
                    }
                }
            }

            var hasDiff = false;
            foreach (var (key, typeName) in resources.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var udpType = ResourceTypeRegistry.ItemTypeMap.GetValueOrDefault(typeName, typeName);
                var localDef = deployer.BuildItemDefinition(key, udpType);
                if (localDef is null)
                {
                    continue;
                }
                if (!existing.TryGetValue(key, out var itemInfo))
                {
                    Ansi.MarkupLine($"[green]+ {Markup.Escape(key)}[/]: new (not yet deployed)");
                    hasDiff = true;
                    continue;
                }

                try
                {
                    var remoteDef = client.GetItemDefinition(workspaceId, itemInfo["id"]!.ToString()!);
                    var remoteParts = remoteDef["definition"]?["parts"] as JsonArray ?? [];
                    var localParts = localDef["parts"] as JsonArray ?? [];

                    foreach (var localPart in localParts)
                    {
                        var localPath = localPart?["path"]?.GetValue<string>() ?? "";
                        var localContent = Decode(localPart?["payload"]?.GetValue<string>());
                        var remoteContent = "";
                        foreach (var rp in remoteParts)
                        {
                            if (rp?["path"]?.GetValue<string>() == localPath)
                            {
                                remoteContent = Decode(rp?["payload"]?.GetValue<string>());
                                break;
                            }
                        }
                        if (localContent != remoteContent)
                        {
                            hasDiff = true;
                            foreach (var line in DiffUtil.UnifiedDiff(remoteContent, localContent, $"deployed/{key}/{localPath}", $"local/{key}/{localPath}"))
                            {
                                if (line.StartsWith('+'))
                                {
                                    Ansi.MarkupLine($"[green]{Markup.Escape(line)}[/]");
                                }
                                else if (line.StartsWith('-'))
                                {
                                    Ansi.MarkupLine($"[red]{Markup.Escape(line)}[/]");
                                }
                                else if (line.StartsWith("@@"))
                                {
                                    Ansi.MarkupLine($"[cyan]{Markup.Escape(line)}[/]");
                                }
                                else
                                {
                                    Ansi.WriteLine(line);
                                }
                            }
                            Ansi.WriteLine();
                        }
                    }
                }
                catch (Exception e)
                {
                    Ansi.MarkupLine($"[yellow]{Markup.Escape(key)}: could not fetch remote definition: {Markup.Escape(e.Message)}[/]");
                }
            }

            if (!hasDiff)
            {
                Ansi.MarkupLine("[dim]No differences found.[/]");
            }
            return 0;
        });
        return cmd;
    }

    private static string Decode(string? base64) =>
        string.IsNullOrEmpty(base64) ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(base64));

    // -- graph ---------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, string> GraphTypeColors = new Dictionary<string, string>
    {
        ["lakehouses"] = "#2d6a4f", ["notebooks"] = "#264653", ["pipelines"] = "#e76f51",
        ["warehouses"] = "#f4a261", ["semantic_models"] = "#e9c46a", ["reports"] = "#a8dadc",
        ["environments"] = "#457b9d", ["data_agents"] = "#6d6875", ["eventhouses"] = "#b5838d",
        ["eventstreams"] = "#ffb4a2", ["ml_models"] = "#cdb4db", ["ml_experiments"] = "#ffc8dd",
    };

    private static Command GraphCommand()
    {
        var file = FileOption();
        var target = TargetOption();
        var format = new Option<string>("--format") { Description = "Output format (mermaid, dot, text)", DefaultValueFactory = _ => "mermaid" };
        var output = new Option<string?>("--output", "-o") { Description = "Output file (default: stdout)" };

        var cmd = new Command("graph", "Visualize the deployment dependency graph.") { file, target, format, output };

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

            var graph = Resolver.BuildDependencyGraph(deployment.Resources);
            var fmt = pr.GetValue(format)!;
            var sb = new StringBuilder();

            if (fmt == "mermaid")
            {
                sb.AppendLine("graph TD");
                foreach (var (key, node) in graph)
                {
                    var color = GraphTypeColors.GetValueOrDefault(node.ResourceType, "#666");
                    sb.AppendLine($"    {key}[\"{key}\\n({node.ResourceType})\"]");
                    sb.AppendLine($"    style {key} fill:{color},color:#fff");
                    foreach (var dep in node.DependsOn.Where(graph.ContainsKey))
                    {
                        sb.AppendLine($"    {dep} --> {key}");
                    }
                }
            }
            else if (fmt == "dot")
            {
                sb.AppendLine("digraph deployment {");
                sb.AppendLine("    rankdir=LR;");
                sb.AppendLine("    node [shape=box, style=filled, fontcolor=white];");
                foreach (var (key, node) in graph)
                {
                    var color = GraphTypeColors.GetValueOrDefault(node.ResourceType, "#666666");
                    sb.AppendLine($"    \"{key}\" [label=\"{key}\\n{node.ResourceType}\", fillcolor=\"{color}\"];");
                    foreach (var dep in node.DependsOn.Where(graph.ContainsKey))
                    {
                        sb.AppendLine($"    \"{dep}\" -> \"{key}\";");
                    }
                }
                sb.AppendLine("}");
            }
            else
            {
                foreach (var (key, node) in graph)
                {
                    var deps = node.DependsOn.Count > 0 ? $" ← {string.Join(", ", node.DependsOn)}" : "";
                    sb.AppendLine($"  [{node.ResourceType}] {key}{deps}");
                }
            }

            var outputText = sb.ToString().TrimEnd('\n', '\r');
            var outputFile = pr.GetValue(output);
            if (!string.IsNullOrEmpty(outputFile))
            {
                File.WriteAllText(outputFile, outputText);
                Ansi.MarkupLine($"Graph written to {Markup.Escape(outputFile)}");
            }
            else
            {
                Ansi.WriteLine(outputText);
            }
            return 0;
        });
        return cmd;
    }

    // -- check-update --------------------------------------------------------

    private static Command CheckUpdateCommand()
    {
        var cmd = new Command("check-update", "Check if a newer version is available on NuGet.");
        cmd.SetAction(_ =>
        {
            try
            {
                var current = typeof(CliApp).Assembly.GetName().Version?.ToString(3) ?? "1.0.1";
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var json = http.GetStringAsync("https://api.nuget.org/v3-flatcontainer/udpi30/index.json").GetAwaiter().GetResult();
                var versions = (JsonNode.Parse(json)?["versions"] as JsonArray)?.Select(v => v!.GetValue<string>()).ToList() ?? [];
                var latest = versions.LastOrDefault() ?? current;

                if (latest != current)
                {
                    Ansi.MarkupLine($"  Update available: [bold]{current}[/] → [bold green]{latest}[/]");
                    Ansi.MarkupLine("  Run: dotnet tool update -g udpi30");
                }
                else
                {
                    Ansi.MarkupLine($"  You're on the latest version: {current}");
                }
            }
            catch
            {
                Ansi.MarkupLine("  [dim]Could not check for updates.[/]");
            }
            return 0;
        });
        return cmd;
    }
}
