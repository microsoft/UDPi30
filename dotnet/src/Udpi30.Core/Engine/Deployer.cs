using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Spectre.Console;
using Udpi30.Core.Engine.State;
using Udpi30.Core.Models;
using Udpi30.Core.Providers;
using Udpi30.Core.Providers.Platforms;

namespace Udpi30.Core.Engine;

/// <summary>Result of a deployment.</summary>
public sealed class DeployResult
{
    public bool Success { get; set; } = true;
    public int ItemsCreated { get; set; }
    public int ItemsUpdated { get; set; }
    public int ItemsDeleted { get; set; }
    public int ItemsSkipped { get; set; }
    public int ItemsFailed { get; set; }
    public List<string> Errors { get; set; } = [];
    public Dictionary<string, string> ItemIds { get; set; } = [];
    public List<string> RollbackLog { get; set; } = [];
    public List<string> HookWarnings { get; set; } = [];
}

/// <summary>
/// Executes a deployment plan against a Fabric workspace — orchestrated create,
/// update, and delete in dependency order with rollback on failure. Mirrors
/// <c>engine/deployer.py</c>.
/// </summary>
public sealed partial class Deployer
{
    private readonly FabricClient _client;
    private readonly DeploymentDefinition _deployment;
    private readonly string _projectDir;
    private readonly IAnsiConsole _console;
    private readonly bool _dryRun;
    private readonly List<(string ResourceKey, string ItemId)> _rollbackStack = [];
    private GraphClient? _graphClient;
    private string? _currentWorkspaceId;
    private bool _forceDeploy;
    private bool _foldersByType;
    private Dictionary<string, string>? _folderIdsByName;
    private readonly Dictionary<ResourcePlatform, IResourcePlatformProvider> _platformProviders = [];

    public StateManager? StateManager { get; set; }

    /// <summary>
    /// When true, a failed item does not trigger rollback — successfully created
    /// items are kept and the deployment is finalized as a partial success.
    /// </summary>
    public bool ContinueOnError { get; set; }

    public Deployer(FabricClient client, DeploymentDefinition deployment, string projectDir,
        IAnsiConsole? console = null, bool dryRun = false)
    {
        _client = client;
        _deployment = deployment;
        _projectDir = projectDir;
        _console = console ?? AnsiConsole.Console;
        _dryRun = dryRun;
    }

    /// <summary>Format a deployment error with actionable guidance (verbatim hints from the Python port).</summary>
    public static string FormatDeployError(string resourceKey, string resourceType, Exception error)
    {
        var msg = error.Message;
        var hints = new List<string>();
        var lower = msg.ToLowerInvariant();

        if (msg.Contains("DisplayName is Invalid"))
            hints.Add("Resource names for lakehouses/warehouses can only contain letters, numbers, and underscores.");
        else if (msg.Contains("ItemDisplayNameNotAvailableYet"))
            hints.Add("The name is temporarily unavailable (recently deleted). Wait a few minutes and retry.");
        else if (msg.Contains("NotebookId") && msg.Contains("cannot be null"))
            hints.Add("Pipeline references notebooks by ID. Ensure the notebook is deployed before the pipeline.");
        else if (msg.Contains("InvalidDefinitionFormat"))
            hints.Add("The notebook definition format is invalid. Ensure .py files are valid Python.");
        else if (msg.Contains("MissingDefinition"))
            hints.Add("This item type requires a definition file (e.g., TMDL for semantic models, PBIR for reports).");
        else if (lower.Contains("capacityid") || lower.Contains("capacity"))
            hints.Add("Check that capacity_id is a valid GUID. Run: az rest --method get --url 'https://api.fabric.microsoft.com/v1/capacities' --resource 'https://api.fabric.microsoft.com'");
        else if (msg.Contains("Unauthorized") || msg.Contains("401"))
            hints.Add("Authentication failed. Run 'az login' or check your service principal credentials.");
        else if (msg.Contains("Forbidden") || msg.Contains("403"))
            hints.Add("You don't have permission. Check your workspace role or capacity access.");
        else if (msg.Contains("feature is not available"))
            hints.Add("This item type requires a feature that's not enabled on your capacity.");
        else if (msg.Contains("UniversalSecurityFeatureDisabled"))
            hints.Add("OneLake security must be enabled on this item. Open the lakehouse in the portal → Manage OneLake security → Enable.");

        var result = $"  ERROR {resourceKey}: {msg}";
        if (hints.Count > 0)
        {
            result += "\n    Hint: " + string.Join(" ", hints);
        }
        return result;
    }

    // -- path / file helpers -------------------------------------------------

    private string ResolvePath(string relativePath) => Path.Combine(_projectDir, relativePath);

    private string ReadFileAsBase64(string path)
    {
        var full = ResolvePath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"File not found: {full}");
        }
        return Convert.ToBase64String(File.ReadAllBytes(full));
    }

    private string ReadFileText(string path)
    {
        var full = ResolvePath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"File not found: {full}");
        }
        return File.ReadAllText(full);
    }

    private static JsonObject Part(string path, string payload) => new()
    {
        ["path"] = path,
        ["payload"] = payload,
        ["payloadType"] = "InlineBase64",
    };

    private static string Base64Json(JsonNode node) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(node.ToJsonString()));

    // -- definition builders -------------------------------------------------

    private JsonObject? BuildGenericDefinition(string path, string partName)
    {
        var content = ReadFileAsBase64(path);
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }
        return new JsonObject { ["parts"] = new JsonArray(Part(partName, content)) };
    }

    private JsonObject? BuildNotebookDefinition(string resourceKey)
    {
        if (!_deployment.Resources.Notebooks.TryGetValue(resourceKey, out var nb))
        {
            return null;
        }

        var fileExt = Path.GetExtension(nb.Path).ToLowerInvariant();

        if (fileExt == ".ipynb")
        {
            return new JsonObject
            {
                ["format"] = "ipynb",
                ["parts"] = new JsonArray(Part("artifact.content.ipynb", ReadFileAsBase64(nb.Path))),
            };
        }

        var rawContent = ReadFileText(nb.Path);
        var langMap = new Dictionary<string, string> { [".py"] = "python", [".sql"] = "sql", [".scala"] = "scala", [".r"] = "r" };
        var kernelMap = new Dictionary<string, string>
        {
            ["python"] = "synapse_pyspark", ["sql"] = "sparksql", ["scala"] = "spark_scala", ["r"] = "sparkr",
        };
        var language = langMap.GetValueOrDefault(fileExt, "python");
        var kernel = kernelMap.GetValueOrDefault(language, "synapse_pyspark");

        var ipynb = new JsonObject
        {
            ["nbformat"] = 4,
            ["nbformat_minor"] = 5,
            ["cells"] = new JsonArray(new JsonObject
            {
                ["cell_type"] = "code",
                ["source"] = new JsonArray(rawContent),
                ["execution_count"] = null,
                ["outputs"] = new JsonArray(),
                ["metadata"] = new JsonObject(),
            }),
            ["metadata"] = new JsonObject
            {
                ["language_info"] = new JsonObject { ["name"] = language },
                ["kernel_info"] = new JsonObject { ["name"] = kernel },
            },
        };

        return new JsonObject
        {
            ["format"] = "ipynb",
            ["parts"] = new JsonArray(Part("artifact.content.ipynb", Base64Json(ipynb))),
        };
    }

    private JsonObject? BuildSparkJobDefinition(string resourceKey)
    {
        if (!_deployment.Resources.SparkJobDefinitions.TryGetValue(resourceKey, out var sjd) || string.IsNullOrEmpty(sjd.Path))
        {
            return null;
        }
        var fileExt = Path.GetExtension(sjd.Path).ToLowerInvariant();
        var argsStr = sjd.Args.Count > 0 ? string.Join(" ", sjd.Args) : "";

        JsonObject SjdJson(string lang) => new()
        {
            ["executableFile"] = null,
            ["defaultLakehouseArtifactId"] = "",
            ["mainClass"] = "",
            ["additionalLakehouseIds"] = new JsonArray(),
            ["retryPolicy"] = null,
            ["commandLineArguments"] = argsStr,
            ["additionalLibraryUris"] = new JsonArray(),
            ["language"] = lang,
            ["environmentArtifactId"] = null,
        };

        if (fileExt == ".jar")
        {
            return new JsonObject
            {
                ["parts"] = new JsonArray(
                    Part("SparkJobDefinitionV1.json", Base64Json(SjdJson("Java"))),
                    Part(Path.GetFileName(sjd.Path), ReadFileAsBase64(sjd.Path))),
            };
        }
        return new JsonObject
        {
            ["parts"] = new JsonArray(Part("SparkJobDefinitionV1.json", Base64Json(SjdJson("Python")))),
        };
    }

    private JsonObject? BuildPipelineDefinition(string resourceKey, string? workspaceId = null)
    {
        if (!_deployment.Resources.Pipelines.TryGetValue(resourceKey, out var pipeline))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(pipeline.Path))
        {
            return new JsonObject { ["parts"] = new JsonArray(Part("pipeline-content.json", ReadFileAsBase64(pipeline.Path))) };
        }

        if (pipeline.Activities.Count == 0)
        {
            return null;
        }

        var itemsMap = new Dictionary<string, Dictionary<string, object?>>();
        if (!string.IsNullOrEmpty(workspaceId))
        {
            try
            {
                itemsMap = _client.GetWorkspaceItemsMap(workspaceId);
            }
            catch
            {
                // Best-effort; IDs may be empty.
            }
        }

        var activities = new JsonArray();
        foreach (var activity in pipeline.Activities)
        {
            var actDef = new JsonObject
            {
                ["name"] = activity.Name ?? activity.Notebook ?? activity.Pipeline ?? "unnamed",
                ["type"] = activity.Notebook is not null ? "TridentNotebook" : "ExecutePipeline",
            };

            if (activity.Notebook is not null)
            {
                var nbId = itemsMap.GetValueOrDefault(activity.Notebook)?.GetValueOrDefault("id")?.ToString() ?? "";
                if (string.IsNullOrEmpty(nbId))
                {
                    _console.MarkupLine($"    [yellow]Warning:[/] Notebook '{Markup.Escape(activity.Notebook)}' not found in workspace — pipeline may fail");
                }
                var typeProps = new JsonObject { ["notebookId"] = nbId };
                if (!string.IsNullOrEmpty(workspaceId)) typeProps["workspaceId"] = workspaceId;
                if (activity.Parameters.Count > 0)
                {
                    var paramObj = new JsonObject();
                    foreach (var (k, v) in activity.Parameters)
                    {
                        paramObj[k] = new JsonObject { ["value"] = JsonValue.Create(v?.ToString()), ["type"] = "string" };
                    }
                    typeProps["parameters"] = paramObj;
                }
                actDef["typeProperties"] = typeProps;
            }
            else if (activity.Pipeline is not null)
            {
                var pipeId = itemsMap.GetValueOrDefault(activity.Pipeline)?.GetValueOrDefault("id")?.ToString() ?? "";
                var typeProps = new JsonObject { ["pipelineId"] = pipeId };
                if (!string.IsNullOrEmpty(workspaceId)) typeProps["workspaceId"] = workspaceId;
                actDef["typeProperties"] = typeProps;
            }

            if (activity.DependsOn.Count > 0)
            {
                actDef["dependsOn"] = new JsonArray(activity.DependsOn
                    .Select(dep => (JsonNode)new JsonObject
                    {
                        ["activity"] = dep,
                        ["dependencyConditions"] = new JsonArray("Succeeded"),
                    }).ToArray());
            }

            activities.Add(actDef);
        }

        var pipelineJson = new JsonObject { ["properties"] = new JsonObject { ["activities"] = activities } };
        return new JsonObject { ["parts"] = new JsonArray(Part("pipeline-content.json", Base64Json(pipelineJson))) };
    }

    private JsonObject? BuildDirectoryDefinition(string path)
    {
        var full = ResolvePath(path);
        if (Directory.Exists(full))
        {
            var parts = new JsonArray();
            foreach (var file in Directory.GetFiles(full, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(full, file).Replace('\\', '/');
                parts.Add(Part(relative, Convert.ToBase64String(File.ReadAllBytes(file))));
            }
            return parts.Count > 0 ? new JsonObject { ["parts"] = parts } : null;
        }
        // No directory and no single file — treat as "no definition" so the caller
        // skips gracefully (consistent with semantic models) instead of throwing.
        if (!File.Exists(full))
        {
            return null;
        }
        return new JsonObject { ["parts"] = new JsonArray(Part(Path.GetFileName(full), ReadFileAsBase64(path))) };
    }

    private JsonObject? BuildSemanticModelDefinition(string resourceKey)
    {
        if (!_deployment.Resources.SemanticModels.TryGetValue(resourceKey, out var sm))
        {
            return null;
        }
        var modelDir = ResolvePath(sm.Path);
        if (!Directory.Exists(modelDir))
        {
            return null;
        }
        var parts = new JsonArray();
        foreach (var file in Directory.GetFiles(modelDir, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(modelDir, file).Replace('\\', '/');
            parts.Add(Part(relative, Convert.ToBase64String(File.ReadAllBytes(file))));
        }
        return parts.Count > 0 ? new JsonObject { ["parts"] = parts } : null;
    }

    private JsonObject? BuildReportDefinition(string resourceKey)
    {
        if (!_deployment.Resources.Reports.TryGetValue(resourceKey, out var report))
        {
            return null;
        }
        var definition = BuildDirectoryDefinition(report.Path);
        if (definition is not null)
        {
            RebindReportToSemanticModel(definition, report);
        }
        return definition;
    }

    /// <summary>
    /// Rewrite a report's <c>definition.pbir</c> to reference the deployed semantic
    /// model by connection id. The Fabric REST API requires a <c>byConnection</c>
    /// reference (a <c>byPath</c> reference only works in Power BI Desktop / Git), so
    /// we inject the just-created model's item id at deploy time.
    /// </summary>
    private void RebindReportToSemanticModel(JsonObject definition, ReportResource report)
    {
        if (string.IsNullOrEmpty(report.SemanticModel) || string.IsNullOrEmpty(_currentWorkspaceId))
        {
            return;
        }
        if (definition["parts"] is not JsonArray parts)
        {
            return;
        }

        JsonObject? pbirPart = null;
        foreach (var part in parts)
        {
            if (part?["path"]?.GetValue<string>() == "definition.pbir")
            {
                pbirPart = part.AsObject();
                break;
            }
        }
        if (pbirPart is null)
        {
            return;
        }

        string? modelId;
        try
        {
            var items = _client.GetWorkspaceItemsMap(_currentWorkspaceId);
            modelId = items.GetValueOrDefault(report.SemanticModel)?.GetValueOrDefault("id")?.ToString();
        }
        catch
        {
            return;
        }
        if (string.IsNullOrEmpty(modelId))
        {
            return;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(pbirPart["payload"]!.GetValue<string>()));
            var pbir = JsonNode.Parse(json)!.AsObject();
            pbir["datasetReference"] = new JsonObject
            {
                ["byConnection"] = new JsonObject { ["connectionString"] = $"semanticmodelid={modelId}" },
            };
            pbirPart["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(pbir.ToJsonString()));
        }
        catch
        {
            // Leave the original definition.pbir untouched if it can't be parsed.
        }
    }

    private JsonObject? DetectReportSchemaVersion(string workspaceId)
    {
        try
        {
            foreach (var item in _client.ListItems(workspaceId, itemType: "Report"))
            {
                try
                {
                    var defn = _client.GetItemDefinition(workspaceId, item["id"]!.GetValue<string>());
                    if (defn["definition"]?["parts"] is JsonArray parts)
                    {
                        foreach (var part in parts)
                        {
                            if (part?["path"]?.GetValue<string>() == "definition/version.json")
                            {
                                var content = Encoding.UTF8.GetString(Convert.FromBase64String(part["payload"]!.GetValue<string>()));
                                return JsonNode.Parse(content) as JsonObject;
                            }
                        }
                    }
                }
                catch
                {
                    // Skip this report.
                }
            }
        }
        catch
        {
            // No reports / no access.
        }
        return null;
    }

    private static readonly IReadOnlyDictionary<string, (string Field, string PartName)> DefinitionPartMap =
        new Dictionary<string, (string, string)>
        {
            ["Dataflow"] = ("dataflows", "dataflow.json"),
            ["GraphQLApi"] = ("graphql_apis", "schema.graphql"),
            ["CopyJob"] = ("copy_jobs", "copyjob.json"),
            ["ApacheAirflowJob"] = ("airflow_jobs", "dag.py"),
            ["Reflex"] = ("reflex", "reflex.json"),
            ["UserDataFunction"] = ("user_data_functions", "function.json"),
            ["Eventstream"] = ("eventstreams", "eventstream.json"),
            ["KQLDashboard"] = ("kql_dashboards", "definition.json"),
            ["KQLQueryset"] = ("kql_querysets", "definition.json"),
            ["Ontology"] = ("ontologies", "definition.json"),
            ["Graph"] = ("graphs", "definition.json"),
            ["DataBuildToolJob"] = ("dbt_jobs", "dbt-project.json"),
            ["AnomalyDetector"] = ("anomaly_detectors", "definition.json"),
            ["DigitalTwinBuilder"] = ("digital_twin_builders", "definition.json"),
            ["DigitalTwinBuilderFlow"] = ("digital_twin_builder_flows", "definition.json"),
            ["EventSchemaSet"] = ("event_schema_sets", "definition.json"),
            ["GraphQuerySet"] = ("graph_query_sets", "definition.json"),
            ["Map"] = ("map_items", "definition.json"),
            ["GraphModel"] = ("graph_models", "definition.json"),
            ["HLSCohort"] = ("hls_cohorts", "definition.json"),
        };

    /// <summary>Public accessor for building an item's Fabric definition (used by plan/diff).</summary>
    public JsonObject? BuildItemDefinition(string resourceKey, string resourceType) =>
        GetItemDefinition(resourceKey, resourceType);

    private JsonObject? GetItemDefinition(string resourceKey, string resourceType)
    {
        if (resourceType == "DataPipeline")
        {
            return BuildPipelineDefinition(resourceKey, _currentWorkspaceId);
        }
        if (resourceType == "Notebook")
        {
            return BuildNotebookDefinition(resourceKey);
        }
        if (resourceType == "SemanticModel")
        {
            return BuildSemanticModelDefinition(resourceKey);
        }
        if (resourceType == "Report")
        {
            return BuildReportDefinition(resourceKey);
        }
        if (resourceType == "SparkJobDefinition")
        {
            if (_deployment.Resources.SparkJobDefinitions.TryGetValue(resourceKey, out var sjd) && !string.IsNullOrEmpty(sjd.Path))
            {
                return BuildSparkJobDefinition(resourceKey);
            }
            return null;
        }
        if (DefinitionPartMap.TryGetValue(resourceType, out var entry))
        {
            var resource = _deployment.Resources.GetResourceObject(entry.Field, resourceKey);
            var path = resource?.GetType().GetProperty("Path")?.GetValue(resource) as string;
            if (!string.IsNullOrEmpty(path))
            {
                return BuildGenericDefinition(path, entry.PartName);
            }
            return null;
        }
        return null;
    }

    private string? GetDescription(string resourceKey, string resourceTypeName)
    {
        var resource = _deployment.Resources.GetResourceObject(resourceTypeName, resourceKey);
        return resource?.GetType().GetProperty("Description")?.GetValue(resource) as string;
    }

    // -- principals / security ----------------------------------------------

    private string? ResolvePrincipalId(string value, string principalType)
    {
        if (GraphClient.IsGuid(value))
        {
            return value;
        }
        try
        {
            _graphClient ??= new GraphClient();
            var resolved = _graphClient.ResolvePrincipal(value, principalType);
            if (resolved is not null)
            {
                _console.MarkupLine($"    Resolved '{Markup.Escape(value)}' → {Markup.Escape(resolved)}");
            }
            return resolved;
        }
        catch
        {
            return null;
        }
    }

    private void DeploySecurity(string workspaceId)
    {
        if (_deployment.Security.Roles.Count == 0)
        {
            return;
        }
        _console.MarkupLine("  Applying security roles...");
        foreach (var role in _deployment.Security.Roles)
        {
            var principalValue = role.EntraGroup ?? role.EntraUser ?? role.ServicePrincipal;
            if (string.IsNullOrEmpty(principalValue))
            {
                continue;
            }
            var principalType = role.EntraUser is not null ? "User"
                : role.ServicePrincipal is not null ? "ServicePrincipal" : "Group";

            var principalId = ResolvePrincipalId(principalValue, principalType);
            if (principalId is null)
            {
                _console.MarkupLine($"    [yellow]Warning:[/] Could not resolve '{Markup.Escape(principalValue)}' to a GUID. Skipping.");
                continue;
            }

            var fabricRole = role.WorkspaceRole switch
            {
                WorkspaceRole.Admin => "Admin",
                WorkspaceRole.Member => "Member",
                WorkspaceRole.Contributor => "Contributor",
                _ => "Viewer",
            };

            if (_dryRun)
            {
                _console.MarkupLine($"    [dim]Would assign {fabricRole} to {Markup.Escape(principalId)}[/]");
            }
            else
            {
                try
                {
                    _client.AddWorkspaceRoleAssignment(workspaceId, principalId, principalType, fabricRole);
                    _console.MarkupLine($"    Assigned {fabricRole} to {Markup.Escape(principalId)}");
                }
                catch (Exception e)
                {
                    _console.MarkupLine($"    [yellow]Warning:[/] Could not assign role: {Markup.Escape(e.Message)}");
                }
            }
        }
    }

    private void DeployGitIntegration(string workspaceId)
    {
        var git = _deployment.Workspace.GitIntegration;
        if (git is null)
        {
            return;
        }
        if (_dryRun)
        {
            _console.MarkupLine($"  [dim]Would connect workspace to git: {Markup.Escape(git.Repository ?? "")}[/]");
            return;
        }
        _console.MarkupLine($"  Connecting workspace to git: {Markup.Escape(git.Repository ?? "")}");
        try
        {
            _client.ConnectWorkspaceToGit(workspaceId, git.Provider, git.Organization ?? "", git.Project,
                git.Repository ?? "", git.Branch, git.Directory);
            _client.InitializeGitConnection(workspaceId);
            _console.MarkupLine("  Git integration configured.");
        }
        catch (Exception e)
        {
            _console.MarkupLine($"  [yellow]Warning:[/] Git integration failed: {Markup.Escape(e.Message)}");
        }
    }

    private Dictionary<string, string> DeployConnections()
    {
        var connectionIds = new Dictionary<string, string>();
        if (_deployment.Connections.Count == 0)
        {
            return connectionIds;
        }
        _console.MarkupLine("  Deploying connections...");

        foreach (var (name, conn) in _deployment.Connections)
        {
            if (_dryRun)
            {
                _console.MarkupLine($"    [dim]Would create connection: {Markup.Escape(name)}[/]");
                continue;
            }
            try
            {
                var typeValue = EnumValueOf(conn.Type);
                var details = new Dictionary<string, object?> { ["type"] = typeValue };
                if (!string.IsNullOrEmpty(conn.Endpoint)) details["endpoint"] = conn.Endpoint;
                if (!string.IsNullOrEmpty(conn.Database)) details["database"] = conn.Database;
                foreach (var (k, v) in conn.Properties) details[k] = v;

                if (!string.IsNullOrEmpty(conn.ConnectionStringVar))
                {
                    var connStr = Environment.GetEnvironmentVariable(conn.ConnectionStringVar);
                    if (!string.IsNullOrEmpty(connStr)) details["connectionString"] = connStr;
                }

                try
                {
                    details = new SecretsResolver().ResolveDict(details);
                }
                catch
                {
                    // Best-effort secret resolution.
                }

                var result = _client.CreateConnection(name, typeValue,
                    connectionDetails: System.Text.Json.JsonSerializer.SerializeToNode(details));
                connectionIds[name] = result["id"]?.GetValue<string>() ?? "";
                _console.MarkupLine($"    Created connection: {Markup.Escape(name)}");
            }
            catch (Exception e)
            {
                _console.MarkupLine($"    [yellow]Warning:[/] Connection '{Markup.Escape(name)}' failed: {Markup.Escape(e.Message)}");
            }
        }
        return connectionIds;
    }

    private void ExecuteSqlScripts(string workspaceId)
    {
        foreach (var (key, warehouse) in _deployment.Resources.Warehouses)
        {
            if (warehouse.SqlScripts.Count == 0)
            {
                continue;
            }
            string warehouseId;
            try
            {
                var items = _client.GetWorkspaceItemsMap(workspaceId);
                if (!items.TryGetValue(key, out var info))
                {
                    _console.MarkupLine($"  [yellow]Warning:[/] Warehouse '{Markup.Escape(key)}' not found, skipping SQL scripts");
                    continue;
                }
                warehouseId = info["id"]?.ToString() ?? "";
            }
            catch
            {
                continue;
            }

            foreach (var scriptPath in warehouse.SqlScripts)
            {
                if (_dryRun)
                {
                    _console.MarkupLine($"  [dim]Would execute SQL: {Markup.Escape(scriptPath)}[/]");
                    continue;
                }
                try
                {
                    var sql = ReadFileText(scriptPath);
                    _console.MarkupLine($"  Executing SQL: {Markup.Escape(scriptPath)}");
                    _client.ExecuteSql(workspaceId, warehouseId, sql);
                }
                catch (Exception e)
                {
                    _console.MarkupLine($"  [yellow]Warning:[/] SQL script '{Markup.Escape(scriptPath)}' failed: {Markup.Escape(e.Message)}");
                }
            }
        }
    }

    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex NonAlphanumeric();

    private void DeployOnelakeRoles(string workspaceId)
    {
        if (_deployment.Security.Roles.Count == 0)
        {
            return;
        }
        foreach (var role in _deployment.Security.Roles)
        {
            if (role.OnelakeRoles.Count == 0)
            {
                continue;
            }
            var principalValue = role.EntraGroup ?? role.EntraUser ?? role.ServicePrincipal;
            if (string.IsNullOrEmpty(principalValue))
            {
                continue;
            }
            var principalType = role.EntraUser is not null ? "User"
                : role.ServicePrincipal is not null ? "ServicePrincipal" : "Group";
            var principalId = ResolvePrincipalId(principalValue, principalType);

            foreach (var binding in role.OnelakeRoles)
            {
                var allItems = _client.ListItems(workspaceId);
                foreach (var lhKey in _deployment.Resources.Lakehouses.Keys)
                {
                    try
                    {
                        string? lhId = null;
                        foreach (var wsItem in allItems)
                        {
                            if (wsItem["displayName"]?.GetValue<string>() == lhKey && wsItem["type"]?.GetValue<string>() == "Lakehouse")
                            {
                                lhId = wsItem["id"]!.GetValue<string>();
                                break;
                            }
                        }
                        if (lhId is null)
                        {
                            continue;
                        }

                        var paths = new List<string>();
                        foreach (var table in binding.Tables) paths.Add(table == "*" ? "*" : $"/Tables/{table}");
                        foreach (var folder in binding.Folders) paths.Add(folder == "*" ? "*" : $"/Files/{folder}");
                        if (paths.Count == 0) paths.Add("*");

                        var permissions = binding.Permissions
                            .Select(p => Capitalize(EnumValueOf(p))).ToList();
                        if (permissions.Count == 0) permissions.Add("Read");

                        var safeName = NonAlphanumeric().Replace($"{role.Name}{lhKey}", "");

                        var entraMembers = new JsonArray();
                        if (principalId is not null)
                        {
                            entraMembers.Add(new JsonObject { ["objectId"] = principalId, ["objectType"] = principalType });
                        }

                        var roleDef = new JsonObject
                        {
                            ["name"] = safeName,
                            ["decisionRules"] = new JsonArray(new JsonObject
                            {
                                ["effect"] = "Permit",
                                ["permission"] = new JsonArray(
                                    new JsonObject { ["attributeName"] = "Path", ["attributeValueIncludedIn"] = new JsonArray(paths.Select(p => (JsonNode)p!).ToArray()) },
                                    new JsonObject { ["attributeName"] = "Action", ["attributeValueIncludedIn"] = new JsonArray(permissions.Select(p => (JsonNode)p!).ToArray()) }),
                            }),
                            ["members"] = new JsonObject
                            {
                                ["fabricItemMembers"] = new JsonArray(new JsonObject
                                {
                                    ["itemAccess"] = new JsonArray("ReadAll"),
                                    ["sourcePath"] = $"{workspaceId}/{lhId}",
                                }),
                                ["microsoftEntraMembers"] = entraMembers,
                            },
                        };

                        if (_dryRun)
                        {
                            _console.MarkupLine($"  [dim]Would set OneLake role: {Markup.Escape(role.Name)} on {Markup.Escape(lhKey)}[/]");
                        }
                        else
                        {
                            _client.UpdateLakehouseDataAccessRoles(workspaceId, lhId, new JsonArray(roleDef));
                            _console.MarkupLine($"    OneLake role: {Markup.Escape(safeName)} on {Markup.Escape(lhKey)}");
                        }
                    }
                    catch (Exception e)
                    {
                        _console.MarkupLine($"    [yellow]Warning:[/] OneLake role failed on {Markup.Escape(lhKey)}: {Markup.Escape(e.Message)}");
                    }
                }
            }
        }
    }

    private void PublishEnvironments(string workspaceId)
    {
        foreach (var (key, env) in _deployment.Resources.Environments)
        {
            if (env.Libraries.Count == 0)
            {
                continue;
            }
            try
            {
                var items = _client.GetWorkspaceItemsMap(workspaceId);
                if (!items.TryGetValue(key, out var info))
                {
                    continue;
                }
                if (_dryRun)
                {
                    _console.MarkupLine($"  [dim]Would publish environment: {Markup.Escape(key)} ({env.Libraries.Count} libraries)[/]");
                    continue;
                }
                _console.MarkupLine($"  Publishing environment: {Markup.Escape(key)}...");
                try
                {
                    var id = info["id"]!.ToString();
                    _client.UpdateEnvironmentLibraries(workspaceId, id, env.Libraries);
                    _client.PublishEnvironment(workspaceId, id);
                    _console.MarkupLine($"    Published: {Markup.Escape(key)} ({Markup.Escape(string.Join(", ", env.Libraries))})");
                }
                catch (Exception e)
                {
                    _console.MarkupLine($"    [yellow]Warning:[/] Environment publish failed for {Markup.Escape(key)}: {Markup.Escape(e.Message)}");
                }
            }
            catch (Exception e)
            {
                _console.MarkupLine($"    [yellow]Warning:[/] Environment {Markup.Escape(key)}: {Markup.Escape(e.Message)}");
            }
        }
    }

    private void DeploySchedules(string workspaceId)
    {
        foreach (var (key, pipeline) in _deployment.Resources.Pipelines)
        {
            if (pipeline.Schedule is not { } schedule)
            {
                continue;
            }
            try
            {
                var items = _client.GetWorkspaceItemsMap(workspaceId);
                if (!items.TryGetValue(key, out var info))
                {
                    continue;
                }
                var scheduleConfig = new JsonObject
                {
                    ["enabled"] = schedule.Enabled,
                    ["configuration"] = new JsonObject
                    {
                        ["type"] = "Cron",
                        ["cronExpression"] = schedule.Cron ?? "0 6 * * *",
                        ["startDateTime"] = schedule.StartTime ?? "2024-01-01T00:00:00Z",
                        ["timeZone"] = schedule.Timezone,
                    },
                };
                var id = info["id"]!.ToString();

                if (_dryRun)
                {
                    _console.MarkupLine($"  [dim]Would set schedule for {Markup.Escape(key)}: {Markup.Escape(schedule.Cron ?? "")}[/]");
                }
                else
                {
                    try
                    {
                        _client.CreateItemSchedule(workspaceId, id, scheduleConfig);
                        _console.MarkupLine($"    Schedule: {Markup.Escape(key)} → {Markup.Escape(schedule.Cron ?? "")} ({Markup.Escape(schedule.Timezone)})");
                    }
                    catch
                    {
                        _client.UpdateItemSchedule(workspaceId, id, scheduleConfig);
                        _console.MarkupLine($"    Schedule updated: {Markup.Escape(key)} → {Markup.Escape(schedule.Cron ?? "")}");
                    }
                }
            }
            catch (Exception e)
            {
                _console.MarkupLine($"    [yellow]Warning:[/] Schedule for {Markup.Escape(key)} failed: {Markup.Escape(e.Message)}");
            }
        }
    }

    private void RefreshSemanticModels(string workspaceId)
    {
        foreach (var (key, model) in _deployment.Resources.SemanticModels)
        {
            if (!model.AutoRefresh)
            {
                continue;
            }
            try
            {
                var items = _client.GetWorkspaceItemsMap(workspaceId);
                if (!items.TryGetValue(key, out var info))
                {
                    continue;
                }
                if (_dryRun)
                {
                    _console.MarkupLine($"  [dim]Would refresh semantic model: {Markup.Escape(key)}[/]");
                }
                else
                {
                    _console.MarkupLine($"  Refreshing semantic model: {Markup.Escape(key)}...");
                    _client.RefreshSemanticModel(workspaceId, info["id"]!.ToString());
                    _console.MarkupLine($"    Refresh complete: {Markup.Escape(key)}");
                }
            }
            catch (Exception e)
            {
                _console.MarkupLine($"    [yellow]Warning:[/] Refresh failed for {Markup.Escape(key)}: {Markup.Escape(e.Message)}");
            }
        }
    }

    private void DeployShortcuts(string workspaceId)
    {
        foreach (var (lhKey, lakehouse) in _deployment.Resources.Lakehouses)
        {
            if (lakehouse.Shortcuts.Count == 0)
            {
                continue;
            }
            var items = _client.GetWorkspaceItemsMap(workspaceId);
            if (!items.TryGetValue(lhKey, out var lhInfo))
            {
                continue;
            }
            var lhId = lhInfo["id"]!.ToString();

            var existingNames = new HashSet<string?>();
            try
            {
                foreach (var s in _client.ListShortcuts(workspaceId, lhId))
                {
                    existingNames.Add(s["name"]?.GetValue<string>());
                }
            }
            catch
            {
                // No existing shortcuts.
            }

            foreach (var shortcut in lakehouse.Shortcuts)
            {
                if (existingNames.Contains(shortcut.Name))
                {
                    continue;
                }
                try
                {
                    var targetStr = shortcut.Target ?? "";
                    JsonObject targetConfig;

                    if (targetStr.StartsWith("adls://") || targetStr.StartsWith("abfss://"))
                    {
                        var parts = targetStr.Replace("adls://", "").Replace("abfss://", "").Split('/', 3);
                        var adls = new JsonObject
                        {
                            ["location"] = $"https://{parts[0]}.dfs.core.windows.net",
                            ["subpath"] = parts.Length > 1 ? "/" + string.Join("/", parts.Skip(1)) : "/",
                        };
                        if (!string.IsNullOrEmpty(shortcut.ConnectionId)) adls["connectionId"] = shortcut.ConnectionId;
                        targetConfig = new JsonObject { ["adlsGen2"] = adls };
                    }
                    else if (targetStr.StartsWith("s3://"))
                    {
                        var parts = targetStr.Replace("s3://", "").Split('/', 2);
                        targetConfig = new JsonObject
                        {
                            ["amazonS3"] = new JsonObject
                            {
                                ["location"] = $"https://{parts[0]}.s3.amazonaws.com",
                                ["subpath"] = parts.Length > 1 ? "/" + parts[1] : "/",
                            },
                        };
                    }
                    else if (targetStr.StartsWith("onelake://"))
                    {
                        var parts = targetStr.Replace("onelake://", "").Split('/', 3);
                        targetConfig = new JsonObject
                        {
                            ["oneLake"] = new JsonObject
                            {
                                ["workspaceId"] = parts.Length > 0 ? parts[0] : "",
                                ["itemId"] = parts.Length > 1 ? parts[1] : "",
                                ["path"] = parts.Length > 2 ? "/" + parts[2] : "/",
                            },
                        };
                    }
                    else
                    {
                        targetConfig = new JsonObject { ["adlsGen2"] = new JsonObject { ["location"] = targetStr, ["subpath"] = "/" } };
                    }

                    var shortcutPath = string.IsNullOrEmpty(shortcut.Path) ? "Tables" : shortcut.Path;

                    JsonObject? transformConfig = null;
                    if (shortcut.Transformation is { } t && t.Type == "file" && t.SourceFormat == "csv")
                    {
                        transformConfig = new JsonObject
                        {
                            ["type"] = "csvToDelta",
                            ["properties"] = new JsonObject
                            {
                                ["delimiter"] = ",",
                                ["useFirstRowAsHeader"] = true,
                                ["skipFilesWithErrors"] = true,
                            },
                            ["includeSubfolders"] = false,
                        };
                    }

                    if (_dryRun)
                    {
                        _console.MarkupLine($"  [dim]Would create shortcut: {Markup.Escape(shortcut.Name)} in {Markup.Escape(lhKey)}[/]");
                        if (transformConfig is not null)
                        {
                            _console.MarkupLine($"  [dim]  with transform: {transformConfig["type"]}[/]");
                        }
                    }
                    else
                    {
                        _client.CreateShortcut(workspaceId, lhId, shortcut.Name, shortcutPath, targetConfig, transformConfig);
                        var xformLabel = transformConfig is not null ? $" (transform: {transformConfig["type"]})" : "";
                        _console.MarkupLine($"    Shortcut: {Markup.Escape(shortcut.Name)} → {Markup.Escape(targetStr)}{Markup.Escape(xformLabel)}");
                    }
                }
                catch (Exception e)
                {
                    _console.MarkupLine($"    [yellow]Warning:[/] Shortcut {Markup.Escape(shortcut.Name)} on {Markup.Escape(lhKey)}: {Markup.Escape(e.Message)}");
                }
            }
        }
    }

    private List<string> RunPostDeployValidation(string workspaceId, string? targetName)
    {
        var target = _deployment.ResolveTarget(targetName);
        if (target.PostDeploy.Count == 0)
        {
            return [];
        }
        _console.MarkupLine("  Running post-deploy validation...");
        var failures = new List<string>();

        foreach (var check in target.PostDeploy)
        {
            if (!string.IsNullOrEmpty(check.Run))
            {
                try
                {
                    var items = _client.GetWorkspaceItemsMap(workspaceId);
                    if (items.TryGetValue(check.Run, out var info))
                    {
                        var jobType = info.GetValueOrDefault("type")?.ToString() == "Notebook" ? "RunNotebook" : "Pipeline";
                        _client.RunItemJob(workspaceId, info["id"]!.ToString()!, jobType);
                        _console.MarkupLine($"    [green]✓[/] {Markup.Escape(check.Run)}: triggered");
                    }
                    else
                    {
                        failures.Add($"{check.Run}: not found in workspace");
                    }
                }
                catch (Exception e)
                {
                    failures.Add($"{check.Run}: {e.Message}");
                }
            }
            else if (!string.IsNullOrEmpty(check.Sql))
            {
                _console.MarkupLine("    [dim]SQL validation not yet wired to endpoint[/]");
            }
        }
        return failures;
    }

    private void Rollback(string workspaceId, DeployResult result)
    {
        if (_rollbackStack.Count == 0)
        {
            return;
        }
        _console.WriteLine();
        _console.MarkupLine("[yellow]Rolling back created items...[/]");

        for (var i = _rollbackStack.Count - 1; i >= 0; i--)
        {
            var (key, itemId) = _rollbackStack[i];
            if (string.IsNullOrEmpty(itemId))
            {
                continue;
            }
            try
            {
                _client.DeleteItem(workspaceId, itemId);
                result.RollbackLog.Add($"Rolled back: {key}");
                _console.MarkupLine($"  [yellow]-[/] Rolled back: {Markup.Escape(key)}");
            }
            catch (Exception e)
            {
                result.RollbackLog.Add($"Rollback failed for {key}: {e.Message}");
                _console.MarkupLine($"  [red]Rollback failed:[/] {Markup.Escape(key)}: {Markup.Escape(e.Message)}");
            }
        }
    }

    private string EnsureWorkspace(string? targetName)
    {
        var wsConfig = _deployment.GetEffectiveWorkspace(targetName);
        if (!string.IsNullOrEmpty(wsConfig.WorkspaceId))
        {
            return wsConfig.WorkspaceId;
        }
        if (string.IsNullOrEmpty(wsConfig.Name))
        {
            throw new InvalidOperationException("No workspace name or ID specified for target");
        }

        var existing = _client.FindWorkspace(wsConfig.Name);
        if (existing is not null)
        {
            return existing["id"]!.GetValue<string>();
        }

        if (_dryRun)
        {
            _console.MarkupLine($"  [dim]Would create workspace: {Markup.Escape(wsConfig.Name)}[/]");
            return "dry-run-workspace-id";
        }

        _console.MarkupLine($"  Creating workspace: {Markup.Escape(wsConfig.Name)}");
        var result = _client.CreateWorkspace(wsConfig.Name, description: wsConfig.Description);
        var workspaceId = result["id"]!.GetValue<string>();

        var capId = wsConfig.EffectiveCapacityId;
        if (!string.IsNullOrEmpty(capId))
        {
            _client.AssignCapacity(workspaceId, capId);
        }
        return workspaceId;
    }

    /// <summary>
    /// The workspace folder a resource should be placed in: an explicit per-item
    /// <c>folder</c> wins; otherwise the per-type folder when
    /// <c>workspace.folders_by_type</c> is enabled; otherwise null (workspace root).
    /// </summary>
    private string? EffectiveFolderName(PlanItem item, string? resourceTypeName)
    {
        if (resourceTypeName is null)
        {
            return null;
        }
        var resource = _deployment.Resources.GetResourceObject(resourceTypeName, item.ResourceKey);
        if (resource?.GetType().GetProperty("Folder")?.GetValue(resource) is string explicitFolder
            && !string.IsNullOrEmpty(explicitFolder))
        {
            return explicitFolder;
        }
        return _foldersByType ? ResourceTypeRegistry.FolderFor(resourceTypeName) : null;
    }

    /// <summary>Resolve (creating if needed) the folder id a new item should be created under.</summary>
    private string? ResolveItemFolderId(string workspaceId, PlanItem item, string? resourceTypeName)
    {
        var folderName = EffectiveFolderName(item, resourceTypeName);
        if (string.IsNullOrEmpty(folderName))
        {
            return null;
        }
        try
        {
            return EnsureFolder(workspaceId, folderName);
        }
        catch (Exception e)
        {
            // Folders are organizational only — never fail a deployment over one.
            _console.MarkupLine($"  [yellow]Warning:[/] could not place '{Markup.Escape(item.ResourceKey)}' in folder '{Markup.Escape(folderName)}': {Markup.Escape(e.Message)} — created at workspace root.");
            return null;
        }
    }

    /// <summary>Return the id of a workspace folder, creating it on first use and caching by name.</summary>
    private string? EnsureFolder(string workspaceId, string name)
    {
        if (_folderIdsByName is null)
        {
            _folderIdsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in _client.ListFolders(workspaceId))
            {
                var displayName = folder["displayName"]?.GetValue<string>();
                var folderId = folder["id"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(folderId))
                {
                    _folderIdsByName[displayName] = folderId;
                }
            }
        }

        if (_folderIdsByName.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var created = _client.CreateFolder(workspaceId, name);
        var newId = created["id"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(newId))
        {
            _folderIdsByName[name] = newId;
            _console.MarkupLine($"  [green]+[/] Created folder: {Markup.Escape(name)}");
            return newId;
        }
        return null;
    }

    /// <summary>The platform that owns a plan item, resolved via its field name in the registry.</summary>
    private ResourcePlatform ResolveItemPlatform(PlanItem item)
    {
        var fieldName = _deployment.Resources.GetResourceType(item.ResourceKey);
        return fieldName is null ? ResourcePlatform.Fabric : ResourceTypeRegistry.PlatformFor(fieldName);
    }

    /// <summary>
    /// Lazily resolve the provider for a non-Fabric platform. Returns <c>null</c>
    /// for Fabric (handled inline by <see cref="DeployItem"/>) or for a platform
    /// with no registered provider yet.
    /// </summary>
    private IResourcePlatformProvider? PlatformProviderFor(ResourcePlatform platform)
    {
        if (platform == ResourcePlatform.Fabric)
        {
            return null;
        }
        if (_platformProviders.TryGetValue(platform, out var existing))
        {
            return existing;
        }
        IResourcePlatformProvider? provider = platform switch
        {
            ResourcePlatform.Entra => new EntraResourceProvider(_graphClient ??= new GraphClient()),
            ResourcePlatform.Azure => new AzureResourceProvider(),
            _ => null,
        };
        if (provider is not null)
        {
            _platformProviders[platform] = provider;
        }
        return provider;
    }

    private PlatformDeployContext BuildPlatformContext() => new()
    {
        Deployment = _deployment,
        ProjectDir = _projectDir,
        Console = _console,
        DryRun = _dryRun,
        // Non-Fabric rollback is owned by each provider (it cannot reuse the
        // Fabric delete-by-item-id stack). Placeholder until Stage 5 wires it.
        RecordRollback = static (_, _) => { },
    };

    /// <summary>Deploy a single non-Fabric item through its platform provider.</summary>
    private bool? DeployViaProvider(PlanItem item, ResourcePlatform platform)
    {
        var provider = PlatformProviderFor(platform);
        if (provider is null)
        {
            _console.MarkupLine($"  [yellow]![/] {Markup.Escape(item.ResourceKey)}: no provider registered for platform {platform} — skipping");
            return null;
        }
        return provider.Apply(item, BuildPlatformContext());
    }

    /// <summary>Deploy a single item. Returns true on success, false on failure, null if skipped.</summary>
    private bool? DeployItem(string workspaceId, PlanItem item, Dictionary<string, Dictionary<string, object?>> existingItems)
    {
        var resourceTypeName = _deployment.Resources.GetResourceType(item.ResourceKey);
        var fabricType = item.ResourceType;

        if (ResourceTypeRegistry.ListOnlyTypes.Contains(fabricType))
        {
            _console.MarkupLine($"  [dim]-[/] {Markup.Escape(item.ResourceKey)}: {fabricType} is list-only (cannot be managed via API)");
            return null;
        }

        if (item.Action == PlanAction.Create)
        {
            var definition = GetItemDefinition(item.ResourceKey, item.ResourceType);
            var description = resourceTypeName is not null ? GetDescription(item.ResourceKey, resourceTypeName) : null;

            if (ResourceTypeRegistry.NoDefinitionTypes.Contains(fabricType))
            {
                definition = null;
            }
            if (ResourceTypeRegistry.DefinitionRequiredTypes.Contains(fabricType) && definition is null)
            {
                _console.MarkupLine($"  [yellow]Warning:[/] {Markup.Escape(item.ResourceKey)}: {fabricType} requires a definition — skipping");
                return null;
            }

            if (_dryRun)
            {
                var plannedFolder = EffectiveFolderName(item, resourceTypeName);
                var folderSuffix = string.IsNullOrEmpty(plannedFolder) ? "" : $" → folder '{plannedFolder}'";
                _console.MarkupLine($"  [green]+[/] Would create {item.ResourceType}: {Markup.Escape(item.ResourceKey)}{Markup.Escape(folderSuffix)}");
                return true;
            }

            JsonObject? creationPayload = null;
            if (item.ResourceType == "Lakehouse"
                && _deployment.Resources.Lakehouses.TryGetValue(item.ResourceKey, out var lh) && lh.EnableSchemas)
            {
                creationPayload = new JsonObject { ["enableSchemas"] = true };
            }

            if (item.ResourceType == "KQLDatabase"
                && _deployment.Resources.KqlDatabases.TryGetValue(item.ResourceKey, out var kdb)
                && !string.IsNullOrEmpty(kdb.ParentEventhouse))
            {
                string? parentId = null;
                for (var wait = 0; wait < 12; wait++)
                {
                    foreach (var wsItem in _client.ListItems(workspaceId))
                    {
                        if (wsItem["displayName"]?.GetValue<string>() == kdb.ParentEventhouse && wsItem["type"]?.GetValue<string>() == "Eventhouse")
                        {
                            parentId = wsItem["id"]!.GetValue<string>();
                            break;
                        }
                    }
                    if (parentId is not null)
                    {
                        break;
                    }
                    _console.MarkupLine($"  [dim]Waiting for eventhouse '{Markup.Escape(kdb.ParentEventhouse)}' to provision...[/]");
                    Thread.Sleep(5000);
                }
                if (parentId is not null)
                {
                    creationPayload = new JsonObject { ["parentEventhouseItemId"] = parentId };
                }
                else
                {
                    _console.MarkupLine($"  [yellow]Warning:[/] Parent eventhouse '{Markup.Escape(kdb.ParentEventhouse)}' not found for KQL database '{Markup.Escape(item.ResourceKey)}'");
                    return false;
                }
            }

            if (item.ResourceType == "DigitalTwinBuilderFlow"
                && _deployment.Resources.DigitalTwinBuilderFlows.TryGetValue(item.ResourceKey, out var dtbf)
                && !string.IsNullOrEmpty(dtbf.TwinBuilder))
            {
                for (var wait = 0; wait < 12; wait++)
                {
                    var found = _client.ListItems(workspaceId).Any(i =>
                        i["displayName"]?.GetValue<string>() == dtbf.TwinBuilder && i["type"]?.GetValue<string>() == "DigitalTwinBuilder");
                    if (found)
                    {
                        break;
                    }
                    _console.MarkupLine($"  [dim]Waiting for twin builder '{Markup.Escape(dtbf.TwinBuilder)}'...[/]");
                    Thread.Sleep(5000);
                }
            }

            if (item.ResourceType == "Report" && definition is not null)
            {
                var detectedVersion = DetectReportSchemaVersion(workspaceId);
                if (detectedVersion is not null && definition["parts"] is JsonArray parts)
                {
                    var versionPayload = Base64Json(detectedVersion);
                    var kept = parts.Where(p => p?["path"]?.GetValue<string>() != "definition/version.json")
                        .Select(p => (JsonNode)p!.DeepClone()).ToList();
                    kept.Add(Part("definition/version.json", versionPayload));
                    definition["parts"] = new JsonArray(kept.ToArray());
                }
            }

            var folderId = ResolveItemFolderId(workspaceId, item, resourceTypeName);
            var result = _client.CreateItem(workspaceId, item.ResourceKey, item.ResourceType,
                definition: definition, description: description, creationPayload: creationPayload, folderId: folderId);

            string? itemId;
            var opUrl = result["operation_url"]?.GetValue<string>();
            if (opUrl is not null)
            {
                try
                {
                    _client.WaitForOperation(opUrl);
                    itemId = _client.GetWorkspaceItemsMap(workspaceId).GetValueOrDefault(item.ResourceKey)?.GetValueOrDefault("id")?.ToString();
                }
                catch
                {
                    itemId = null;
                }
            }
            else
            {
                itemId = result["id"]?.GetValue<string>();
            }

            if (!string.IsNullOrEmpty(itemId))
            {
                _rollbackStack.Add((item.ResourceKey, itemId));
                result["id"] = itemId;
            }
            return !string.IsNullOrEmpty(itemId);
        }

        if (item.Action == PlanAction.Update)
        {
            var itemId = existingItems.GetValueOrDefault(item.ResourceKey)?.GetValueOrDefault("id")?.ToString();
            if (string.IsNullOrEmpty(itemId))
            {
                _console.MarkupLine($"  [yellow]![/] Cannot update {Markup.Escape(item.ResourceKey)}: item ID not found");
                return false;
            }

            var definition = GetItemDefinition(item.ResourceKey, item.ResourceType);
            if (ResourceTypeRegistry.NoDefinitionTypes.Contains(fabricType))
            {
                definition = null;
            }

            if (definition is not null && StateManager is not null && !_forceDeploy)
            {
                var newHash = StateJson.ComputeDefinitionHash(definition);
                var stored = StateManager.Load().Resources.GetValueOrDefault(item.ResourceKey);
                if (stored?.DefinitionHash is { } h && h == newHash)
                {
                    _console.MarkupLine($"  [dim]=[/] {Markup.Escape(item.ResourceKey)}: unchanged, skipping");
                    return true;
                }
            }

            if (_dryRun)
            {
                _console.MarkupLine($"  [yellow]~[/] Would update {item.ResourceType}: {Markup.Escape(item.ResourceKey)}");
                return true;
            }

            if (definition is not null)
            {
                _client.UpdateItemDefinition(workspaceId, itemId, definition);
            }
            var description = resourceTypeName is not null ? GetDescription(item.ResourceKey, resourceTypeName) : null;
            if (!string.IsNullOrEmpty(description))
            {
                _client.UpdateItem(workspaceId, itemId, description: description);
            }
            return true;
        }

        if (item.Action == PlanAction.Delete)
        {
            var itemId = existingItems.GetValueOrDefault(item.ResourceKey)?.GetValueOrDefault("id")?.ToString();
            if (string.IsNullOrEmpty(itemId))
            {
                return true;
            }
            if (_dryRun)
            {
                _console.MarkupLine($"  [red]-[/] Would delete {item.ResourceType}: {Markup.Escape(item.ResourceKey)}");
                return true;
            }
            _client.DeleteItem(workspaceId, itemId);
            return true;
        }

        return true; // NO_CHANGE
    }

    /// <summary>Execute a deployment plan.</summary>
    public DeployResult Execute(DeploymentPlan plan, string? targetName = null, bool force = false)
    {
        var result = new DeployResult { Success = true };
        _forceDeploy = force;

        if (StateManager is not null && !_dryRun)
        {
            var lockInfo = StateManager.GetLockInfo();
            if (lockInfo is not null && !_forceDeploy)
            {
                _console.MarkupLine($"[red]Deployment locked[/] by {lockInfo["owner"]?.ToString() ?? "unknown"} at {lockInfo["timestamp"]?.ToString() ?? "?"}");
                _console.MarkupLine("  Use --force to override.");
                result.Success = false;
                result.Errors.Add("Deployment locked");
                return result;
            }
            StateManager.AcquireLock();
        }

        if (!plan.HasChanges)
        {
            _console.MarkupLine("[dim]No changes to deploy.[/]");
            return result;
        }
        if (plan.Errors.Count > 0)
        {
            result.Success = false;
            result.Errors = plan.Errors;
            return result;
        }

        string workspaceId;
        try
        {
            workspaceId = EnsureWorkspace(targetName);
            _currentWorkspaceId = workspaceId;
            _foldersByType = _deployment.GetEffectiveWorkspace(targetName).FoldersByType ?? false;
            _folderIdsByName = null; // reload the folder cache per deployment run
        }
        catch (Exception e)
        {
            result.Success = false;
            result.Errors.Add($"Failed to ensure workspace: {e.Message}");
            return result;
        }

        var existingItems = new Dictionary<string, Dictionary<string, object?>>();
        if (!_dryRun)
        {
            try
            {
                existingItems = _client.GetWorkspaceItemsMap(workspaceId);
            }
            catch
            {
                // Fresh workspace.
            }
        }

        var actionItems = plan.Items.Where(i => i.Action != PlanAction.NoChange).ToList();

        foreach (var item in actionItems)
        {
            try
            {
                var platform = ResolveItemPlatform(item);
                var success = platform == ResourcePlatform.Fabric
                    ? DeployItem(workspaceId, item, existingItems)
                    : DeployViaProvider(item, platform);
                if (success is null)
                {
                    result.ItemsSkipped++;
                }
                else if (success.Value)
                {
                    switch (item.Action)
                    {
                        case PlanAction.Create: result.ItemsCreated++; break;
                        case PlanAction.Update: result.ItemsUpdated++; break;
                        case PlanAction.Delete: result.ItemsDeleted++; break;
                    }
                }
                else
                {
                    result.ItemsFailed++;
                    result.Errors.Add($"Failed to {item.ActionValue} {item.ResourceKey}");
                }
            }
            catch (FabricApiError e)
            {
                result.ItemsFailed++;
                result.Errors.Add($"{item.ResourceKey}: {e.Message}");
                _console.WriteLine(FormatDeployError(item.ResourceKey, item.ResourceType, e));
            }
            catch (FileNotFoundException e)
            {
                result.ItemsFailed++;
                result.Errors.Add($"{item.ResourceKey}: {e.Message}");
                _console.MarkupLine($"  [red]ERROR[/] {Markup.Escape(item.ResourceKey)}: {Markup.Escape(e.Message)}");
            }
            catch (Exception e)
            {
                result.ItemsFailed++;
                result.Errors.Add($"{item.ResourceKey}: Unexpected error: {e.Message}");
                _console.MarkupLine($"  [red]ERROR[/] {Markup.Escape(item.ResourceKey)}: {Markup.Escape(e.Message)}");
            }
        }

        if (result.ItemsFailed > 0 && _rollbackStack.Count > 0 && !ContinueOnError)
        {
            Rollback(workspaceId, result);
        }
        else if (result.ItemsFailed > 0 && ContinueOnError)
        {
            _console.MarkupLine($"[yellow]Continuing past {result.ItemsFailed} error(s)[/] (--continue-on-error) — created items left in place.");
        }
        result.Success = result.ItemsFailed == 0;

        if ((result.Success || ContinueOnError) && !_dryRun)
        {
            var hookWarnings = new List<string>();
            var hooks = new (string Name, Action Fn)[]
            {
                ("Environment publish", () => PublishEnvironments(workspaceId)),
                ("Security roles", () => DeploySecurity(workspaceId)),
                ("OneLake roles", () => DeployOnelakeRoles(workspaceId)),
                ("Git integration", () => DeployGitIntegration(workspaceId)),
                ("Connections", () => DeployConnections()),
                ("Shortcuts", () => DeployShortcuts(workspaceId)),
                ("Schedules", () => DeploySchedules(workspaceId)),
                ("SQL scripts", () => ExecuteSqlScripts(workspaceId)),
                ("Semantic model refresh", () => RefreshSemanticModels(workspaceId)),
            };
            foreach (var (name, fn) in hooks)
            {
                try
                {
                    fn();
                }
                catch (Exception e)
                {
                    hookWarnings.Add($"{name}: {e.Message}");
                }
            }
            result.HookWarnings = hookWarnings;

            var validationFailures = RunPostDeployValidation(workspaceId, targetName);
            if (validationFailures.Count > 0)
            {
                _console.MarkupLine("[yellow]Post-deploy validation warnings:[/]");
                foreach (var f in validationFailures)
                {
                    _console.MarkupLine($"  [yellow]![/] {Markup.Escape(f)}");
                }
            }
        }

        if ((result.Success || ContinueOnError) && !_dryRun && StateManager is not null)
        {
            var deployedItems = new Dictionary<string, Dictionary<string, object?>>();
            try
            {
                var currentItems = _client.GetWorkspaceItemsMap(workspaceId);
                foreach (var item in plan.Items)
                {
                    if (item.Action is PlanAction.Create or PlanAction.Update)
                    {
                        var live = currentItems.GetValueOrDefault(item.ResourceKey);
                        // With --continue-on-error, failed items never reached the
                        // workspace — only record what actually exists.
                        if (live is null)
                        {
                            continue;
                        }
                        var definition = GetItemDefinition(item.ResourceKey, item.ResourceType);
                        deployedItems[item.ResourceKey] = new Dictionary<string, object?>
                        {
                            ["id"] = live.GetValueOrDefault("id")?.ToString() ?? "",
                            ["type"] = item.ResourceType,
                            ["definition_hash"] = StateJson.ComputeDefinitionHash(definition),
                        };
                    }
                }
            }
            catch
            {
                // Best-effort state capture.
            }

            if (deployedItems.Count > 0)
            {
                var wsConfig = _deployment.GetEffectiveWorkspace(targetName);
                StateManager.RecordDeployment(_deployment.Deployment.Name, _deployment.Deployment.Version,
                    workspaceId, wsConfig.Name ?? "", deployedItems);
            }
        }

        _console.WriteLine();
        if (result.Success)
        {
            _console.MarkupLine("[bold green]Deployment complete.[/]");
        }
        else
        {
            _console.MarkupLine("[bold red]Deployment completed with errors.[/]");
            if (result.RollbackLog.Count > 0)
            {
                _console.MarkupLine("[yellow]Rollback actions:[/]");
                foreach (var entry in result.RollbackLog)
                {
                    _console.MarkupLine($"  {Markup.Escape(entry)}");
                }
            }
        }

        _console.MarkupLine(
            $"  Created: {result.ItemsCreated}  Updated: {result.ItemsUpdated}  " +
            $"Deleted: {result.ItemsDeleted}  Skipped: {result.ItemsSkipped}  Failed: {result.ItemsFailed}");

        if (result.HookWarnings.Count > 0)
        {
            _console.MarkupLine($"  [yellow]Post-deploy warnings: {result.HookWarnings.Count}[/]");
            foreach (var w in result.HookWarnings)
            {
                _console.MarkupLine($"    [yellow]![/] {Markup.Escape(w)}");
            }
        }

        try
        {
            if (StateManager is not null && !_dryRun)
            {
                StateManager.ReleaseLock();
            }
        }
        catch
        {
            // Best-effort lock release.
        }

        return result;
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string EnumValueOf<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Yaml.EnumValueMap.ToString(typeof(TEnum), value);
}
