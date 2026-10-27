using System.Text.Json.Nodes;
using Spectre.Console;
using Udpi30.Core.Models;
using Udpi30.Core.Providers;
using Udpi30.Core.Yaml;

namespace Udpi30.Core.Generators;

/// <summary>
/// Generates a <c>udp.yml</c> from an existing Fabric workspace — the on-ramp
/// for existing projects (<c>udpi30 generate</c>). Mirrors <c>generators/reverse.py</c>.
/// </summary>
public static class ReverseGenerator
{
    // Fabric item type -> our snake_case resource field name. Scoped to Fabric:
    // `generate` reverse-engineers a Fabric workspace, so Azure/Entra rows (which
    // share no item-type namespace) must not appear here.
    private static readonly IReadOnlyDictionary<string, string> ReverseTypeMap =
        ResourceTypeRegistry.ForPlatform(ResourcePlatform.Fabric)
            .ToDictionary(r => r.FabricType, r => r.FieldName);

    private static string SanitizeKey(string name) =>
        name.ToLowerInvariant().Replace(" ", "-").Replace("_", "-");

    /// <summary>Generate a udp.yml structure from a workspace, exporting item definitions to disk.</summary>
    /// <remarks>
    /// Fabric workspace items are always scanned. Set <paramref name="includeEntra"/>
    /// and/or <paramref name="includeAzure"/> to also import the Entra directory
    /// objects and Azure resources currently deployed, so a single <c>generate</c>
    /// reverse-engineers the whole multi-platform footprint. Azure discovery can be
    /// narrowed with <paramref name="subscription"/> and <paramref name="resourceGroup"/>.
    /// </remarks>
    public static Dictionary<string, object?> GenerateDeploymentFromWorkspace(
        FabricClient client, string? workspaceName = null, string? workspaceId = null,
        string? outputDir = null, IAnsiConsole? console = null,
        bool includeEntra = false, bool includeAzure = false,
        string? subscription = null, string? resourceGroup = null, string? azureLocation = null,
        GraphClient? graph = null, AzureCli? az = null)
    {
        console ??= AnsiConsole.Console;
        outputDir ??= Directory.GetCurrentDirectory();

        JsonObject ws;
        if (!string.IsNullOrEmpty(workspaceId))
        {
            ws = client.GetWorkspace(workspaceId);
        }
        else if (!string.IsNullOrEmpty(workspaceName))
        {
            ws = client.FindWorkspace(workspaceName)
                ?? throw new ArgumentException($"Workspace '{workspaceName}' not found");
        }
        else
        {
            throw new ArgumentException("Either workspace_name or workspace_id must be provided");
        }

        var wsId = ws["id"]!.GetValue<string>();
        var wsName = ws["displayName"]?.GetValue<string>() ?? workspaceName ?? "unknown";

        console.MarkupLine($"Scanning workspace: [bold]{Markup.Escape(wsName)}[/] ({Markup.Escape(wsId)})");

        var items = client.ListItems(wsId);
        console.MarkupLine($"  Found {items.Count} items");

        var resourcesOut = new Dictionary<string, object?>();
        var deploymentData = new Dictionary<string, object?>
        {
            ["deployment"] = new Dictionary<string, object?>
            {
                ["name"] = SanitizeKey(wsName),
                ["version"] = "0.1.0",
                ["description"] = $"Generated from workspace: {wsName}",
            },
            ["workspace"] = new Dictionary<string, object?> { ["name"] = wsName },
            ["resources"] = resourcesOut,
            ["targets"] = new Dictionary<string, object?>
            {
                ["dev"] = new Dictionary<string, object?> { ["default"] = true, ["workspace"] = new Dictionary<string, object?> { ["name"] = $"{wsName}-dev" } },
                ["staging"] = new Dictionary<string, object?> { ["workspace"] = new Dictionary<string, object?> { ["name"] = $"{wsName}-staging" } },
                ["prod"] = new Dictionary<string, object?> { ["workspace"] = new Dictionary<string, object?> { ["name"] = wsName } },
            },
        };

        // Group items by resource type.
        var itemsByType = new Dictionary<string, List<JsonObject>>();
        foreach (var node in items)
        {
            var item = node.AsObject();
            var itemType = item["type"]?.GetValue<string>() ?? "Unknown";
            if (ReverseTypeMap.TryGetValue(itemType, out var resourceType))
            {
                if (!itemsByType.TryGetValue(resourceType, out var list))
                {
                    itemsByType[resourceType] = list = [];
                }
                list.Add(item);
            }
            else
            {
                console.MarkupLine($"  [dim]Skipping unsupported item type: {Markup.Escape(itemType)} ({Markup.Escape(item["displayName"]?.GetValue<string>() ?? "")})[/]");
            }
        }

        var metadataOnly = new HashSet<string>
        {
            "warehouses", "eventhouses", "eventstreams", "kql_databases", "kql_dashboards",
            "kql_querysets", "ml_experiments", "ml_models", "graphql_apis", "spark_job_definitions",
            "copy_jobs", "airflow_jobs", "reflex", "variable_libraries", "ontologies",
            "sql_databases", "data_agents",
        };

        foreach (var (resourceType, typeItems) in itemsByType.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            console.MarkupLine($"  Processing {Markup.Escape(resourceType)}: {typeItems.Count} items");
            var resources = new Dictionary<string, object?>();

            foreach (var item in typeItems)
            {
                var displayName = item["displayName"]?.GetValue<string>() ?? "unknown";
                var key = SanitizeKey(displayName);
                var itemId = item["id"]?.GetValue<string>() ?? "";
                var resourceDef = new Dictionary<string, object?>();
                var description = item["description"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(description))
                {
                    resourceDef["description"] = description;
                }

                switch (resourceType)
                {
                    case "notebooks":
                        resourceDef["path"] = $"./notebooks/{displayName}.py";
                        TryExport(client, wsId, itemId, Path.Combine(outputDir, "notebooks"), displayName, console,
                            ok: () => console.MarkupLine($"    Exported: {Markup.Escape(displayName)}"),
                            fail: e =>
                            {
                                console.MarkupLine($"    [yellow]Could not export {Markup.Escape(displayName)}: {Markup.Escape(e.Message)}[/]");
                                resourceDef["path"] = $"./notebooks/{displayName}.py  # TODO: export manually";
                            });
                        break;

                    case "pipelines":
                        resourceDef["path"] = $"./pipelines/{displayName}.json";
                        TryExport(client, wsId, itemId, Path.Combine(outputDir, "pipelines"), displayName, console,
                            ok: () => console.MarkupLine($"    Exported: {Markup.Escape(displayName)}"));
                        break;

                    case "semantic_models":
                        resourceDef["path"] = $"./semantic_models/{displayName}/";
                        TryExport(client, wsId, itemId, Path.Combine(outputDir, "semantic_models", displayName), displayName, console,
                            ok: () => console.MarkupLine($"    Exported TMDL definition: {Markup.Escape(displayName)}"));
                        break;

                    case "reports":
                        resourceDef["path"] = $"./reports/{displayName}/";
                        TryExport(client, wsId, itemId, Path.Combine(outputDir, "reports", displayName), displayName, console,
                            ok: () => console.MarkupLine($"    Exported PBIR definition: {Markup.Escape(displayName)}"));
                        break;

                    case "lakehouses":
                        break; // Metadata-only.

                    case "warehouses":
                        resourceDef["sql_scripts"] = new List<object?>();
                        try
                        {
                            var schemaInfo = ExportWarehouseSchema(client, wsId, itemId);
                            if (schemaInfo is not null)
                            {
                                resourceDef["schema"] = schemaInfo;
                                console.MarkupLine($"    Exported schema: {Markup.Escape(displayName)}");
                            }
                        }
                        catch (Exception e)
                        {
                            console.MarkupLine($"    [yellow]Could not export warehouse schema {Markup.Escape(displayName)}: {Markup.Escape(e.Message)}[/]");
                        }
                        break;

                    case "environments":
                        resourceDef["runtime"] = "1.3";
                        resourceDef["libraries"] = new List<object?>();
                        break;

                    default:
                        if (metadataOnly.Contains(resourceType))
                        {
                            resourceDef["description"] = description ?? "";
                        }
                        else
                        {
                            console.MarkupLine($"  [yellow]Skipping:[/] {Markup.Escape(displayName)} ({Markup.Escape(resourceType)}) — export not supported for this type");
                            continue;
                        }
                        break;
                }

                resources[key] = resourceDef;
            }

            if (resources.Count > 0)
            {
                resourcesOut[resourceType] = resources;
            }
        }

        // Optionally fold in the Entra and Azure resources currently deployed, so
        // `generate` can reverse-engineer the whole multi-platform footprint.
        if (includeEntra)
        {
            console.MarkupLine("Scanning Microsoft Entra (groups, app registrations)…");
            graph ??= new GraphClient();
            MergeDiscovered(resourcesOut, ReverseDiscovery.DiscoverEntra(graph), console);
        }
        if (includeAzure)
        {
            console.MarkupLine("Scanning Azure resources…");
            az ??= new AzureCli();
            MergeDiscovered(resourcesOut, ReverseDiscovery.DiscoverAzure(az, subscription, resourceGroup), console);

            var azureBlock = new Dictionary<string, object?>();
            if (!string.IsNullOrEmpty(subscription))
            {
                azureBlock["subscription"] = subscription;
            }
            if (!string.IsNullOrEmpty(azureLocation))
            {
                azureBlock["location"] = azureLocation;
            }
            if (azureBlock.Count > 0)
            {
                deploymentData["azure"] = azureBlock;
            }
        }

        var outputFile = Path.Combine(outputDir, "udp.yml");
        File.WriteAllText(outputFile, YamlFactory.CreateGenericSerializer().Serialize(deploymentData));

        console.WriteLine();
        console.MarkupLine($"[bold green]Generated:[/] {Markup.Escape(outputFile)}");
        console.WriteLine();
        console.MarkupLine("Next steps:");
        console.MarkupLine("  1. Review and edit udp.yml");
        console.MarkupLine("  2. Export item definitions: udpi30 export");
        console.MarkupLine("  3. Validate: udpi30 validate");
        console.MarkupLine("  4. Deploy to a target: udpi30 deploy -t dev");

        return deploymentData;
    }

    /// <summary>
    /// Fold discovered (Entra/Azure) resources into the reverse-generated
    /// <c>resources</c> map, grouped by snake_case field name. Keys are kept verbatim
    /// — for these platforms the key IS the resource's deployed name (Entra display
    /// name, Azure resource name), so it must not be sanitized like a Fabric item.
    /// </summary>
    private static void MergeDiscovered(Dictionary<string, object?> resourcesOut,
        IEnumerable<DiscoveredResource> discovered, IAnsiConsole console)
    {
        foreach (var group in discovered.GroupBy(d => d.FieldName).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var entries = new Dictionary<string, object?>();
            foreach (var item in group)
            {
                entries[item.Key] = ReverseDiscovery.ToYamlObject(item.Model);
            }
            if (entries.Count > 0)
            {
                resourcesOut[group.Key] = entries;
                console.MarkupLine($"  Imported {Markup.Escape(group.Key)}: {entries.Count} item(s)");
            }
        }
    }

    private static void TryExport(FabricClient client, string wsId, string itemId, string outputDir,
        string name, IAnsiConsole console, Action ok, Action<Exception>? fail = null)
    {
        try
        {
            var defn = client.GetItemDefinition(wsId, itemId);
            if (defn["definition"] is JsonObject definition)
            {
                ExportDefinition(definition, outputDir);
                ok();
            }
        }
        catch (Exception e)
        {
            if (fail is not null)
            {
                fail(e);
            }
            else
            {
                console.MarkupLine($"    [yellow]Could not export {Markup.Escape(name)}: {Markup.Escape(e.Message)}[/]");
            }
        }
    }

    private static Dictionary<string, object?>? ExportWarehouseSchema(FabricClient client, string workspaceId, string warehouseId)
    {
        var tables = new List<object?>();
        var views = new List<object?>();

        var result = client.ExecuteSql(workspaceId, warehouseId,
            "SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_SCHEMA, TABLE_NAME");

        if (result?["results"] is JsonArray results && results.Count > 0 && results[0]?["rows"] is JsonArray rows)
        {
            foreach (var row in rows)
            {
                string tableSchema, tableName, tableType;
                if (row is JsonArray arr)
                {
                    tableSchema = arr[0]?.GetValue<string>() ?? "dbo";
                    tableName = arr[1]?.GetValue<string>() ?? "";
                    tableType = arr[2]?.GetValue<string>() ?? "";
                }
                else
                {
                    tableSchema = row?["TABLE_SCHEMA"]?.GetValue<string>() ?? "dbo";
                    tableName = row?["TABLE_NAME"]?.GetValue<string>() ?? "";
                    tableType = row?["TABLE_TYPE"]?.GetValue<string>() ?? "";
                }
                var entry = $"{tableSchema}.{tableName}";
                if (tableType.ToUpperInvariant().Contains("VIEW"))
                {
                    views.Add(entry);
                }
                else
                {
                    tables.Add(entry);
                }
            }
        }

        if (tables.Count == 0 && views.Count == 0)
        {
            return null;
        }
        return new Dictionary<string, object?> { ["tables"] = tables, ["views"] = views };
    }

    private static void ExportDefinition(JsonObject definition, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        if (definition["parts"] is not JsonArray parts)
        {
            return;
        }
        foreach (var part in parts)
        {
            var path = part?["path"]?.GetValue<string>() ?? "";
            var payload = part?["payload"]?.GetValue<string>() ?? "";
            var payloadType = part?["payloadType"]?.GetValue<string>() ?? "";
            if (payloadType == "InlineBase64")
            {
                var content = Convert.FromBase64String(payload);
                var outPath = Path.Combine(outputDir, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, content);
            }
        }
    }
}
