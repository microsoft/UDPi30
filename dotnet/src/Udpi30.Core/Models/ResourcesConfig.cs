using System.Collections;
using System.Text.RegularExpressions;

namespace Udpi30.Core.Models;

/// <summary>All resource definitions in the deployment.</summary>
public sealed partial class ResourcesConfig
{
    public Dictionary<string, LakehouseResource> Lakehouses { get; set; } = [];
    public Dictionary<string, NotebookResource> Notebooks { get; set; } = [];
    public Dictionary<string, PipelineResource> Pipelines { get; set; } = [];
    public Dictionary<string, WarehouseResource> Warehouses { get; set; } = [];
    public Dictionary<string, SemanticModelResource> SemanticModels { get; set; } = [];
    public Dictionary<string, ReportResource> Reports { get; set; } = [];
    public Dictionary<string, DataAgentInstructions> DataAgents { get; set; } = [];
    public Dictionary<string, EnvironmentResource> Environments { get; set; } = [];
    public Dictionary<string, EventhouseResource> Eventhouses { get; set; } = [];
    public Dictionary<string, EventstreamResource> Eventstreams { get; set; } = [];
    public Dictionary<string, MLModelResource> MlModels { get; set; } = [];
    public Dictionary<string, MLExperimentResource> MlExperiments { get; set; } = [];
    public Dictionary<string, KQLDatabaseResource> KqlDatabases { get; set; } = [];
    public Dictionary<string, KQLDashboardResource> KqlDashboards { get; set; } = [];
    public Dictionary<string, KQLQuerysetResource> KqlQuerysets { get; set; } = [];
    public Dictionary<string, DataflowResource> Dataflows { get; set; } = [];
    public Dictionary<string, GraphQLApiResource> GraphqlApis { get; set; } = [];
    public Dictionary<string, SparkJobDefinitionResource> SparkJobDefinitions { get; set; } = [];
    public Dictionary<string, SQLDatabaseResource> SqlDatabases { get; set; } = [];
    public Dictionary<string, MirroredDatabaseResource> MirroredDatabases { get; set; } = [];
    public Dictionary<string, CopyJobResource> CopyJobs { get; set; } = [];
    public Dictionary<string, ApacheAirflowJobResource> AirflowJobs { get; set; } = [];
    public Dictionary<string, ReflexResource> Reflex { get; set; } = [];
    public Dictionary<string, MountedDataFactoryResource> MountedDataFactories { get; set; } = [];
    public Dictionary<string, UserDataFunctionResource> UserDataFunctions { get; set; } = [];
    public Dictionary<string, VariableLibraryResource> VariableLibraries { get; set; } = [];
    public Dictionary<string, OntologyResource> Ontologies { get; set; } = [];
    public Dictionary<string, GraphResource> Graphs { get; set; } = [];
    public Dictionary<string, DataBuildToolJobResource> DbtJobs { get; set; } = [];
    public Dictionary<string, DatamartResource> Datamarts { get; set; } = [];
    public Dictionary<string, PaginatedReportResource> PaginatedReports { get; set; } = [];
    public Dictionary<string, DashboardResource> Dashboards { get; set; } = [];
    public Dictionary<string, MirroredWarehouseResource> MirroredWarehouses { get; set; } = [];
    public Dictionary<string, SnowflakeDatabaseResource> SnowflakeDatabases { get; set; } = [];
    public Dictionary<string, CosmosDBDatabaseResource> CosmosdbDatabases { get; set; } = [];
    public Dictionary<string, MirroredDatabricksCatalogResource> MirroredDatabricksCatalogs { get; set; } = [];
    public Dictionary<string, OperationsAgentResource> OperationsAgents { get; set; } = [];
    public Dictionary<string, AnomalyDetectorResource> AnomalyDetectors { get; set; } = [];
    public Dictionary<string, DigitalTwinBuilderResource> DigitalTwinBuilders { get; set; } = [];
    public Dictionary<string, DigitalTwinBuilderFlowResource> DigitalTwinBuilderFlows { get; set; } = [];
    public Dictionary<string, EventSchemaSetResource> EventSchemaSets { get; set; } = [];
    public Dictionary<string, GraphQuerySetResource> GraphQuerySets { get; set; } = [];
    public Dictionary<string, MapResource> MapItems { get; set; } = [];
    public Dictionary<string, GraphModelResource> GraphModels { get; set; } = [];
    public Dictionary<string, HLSCohortResource> HlsCohorts { get; set; } = [];
    public Dictionary<string, MaterializedLakeViewResource> MaterializedLakeViews { get; set; } = [];

    // --- Microsoft Entra (Graph, tenant scope) ---
    public Dictionary<string, EntraGroupResource> EntraGroups { get; set; } = [];
    public Dictionary<string, EntraAppResource> EntraApps { get; set; } = [];

    // --- Azure (ARM via Bicep) ---
    public Dictionary<string, AzureResourceGroupResource> AzureResourceGroups { get; set; } = [];
    public Dictionary<string, AzureStorageAccountResource> AzureStorageAccounts { get; set; } = [];
    public Dictionary<string, AzureBicepResource> AzureDeployments { get; set; } = [];

    // --- Azure service resources (generic single-resource Bicep) ---
    // Integration / streaming / compute
    public Dictionary<string, AzureServiceResource> AzureDataFactories { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureDatabricksWorkspaces { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureDatabricksStructuredStreaming { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureEventHubNamespaces { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureEventGridTopics { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureStreamAnalyticsJobs { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureIotHubs { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureLogicApps { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureFunctions { get; set; } = [];

    // Storage family
    public Dictionary<string, AzureServiceResource> AzureBlobStorage { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureDataLakeStorage { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureFiles { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureQueueStorage { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureTableStorage { get; set; } = [];

    // Databases
    public Dictionary<string, AzureServiceResource> AzureSqlDatabases { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureSqlManagedInstances { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureSqlVirtualMachines { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzurePostgresql { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureMysql { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureMariadb { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureCosmosdbAccounts { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureRedisCache { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureDataBox { get; set; } = [];

    // Governance / security / AI
    public Dictionary<string, AzureServiceResource> AzurePurviewAccounts { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureKeyVaults { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzurePolicyAssignments { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureDefenderPlans { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureMachineLearningWorkspaces { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureAiFoundry { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureSentinel { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureVideoIndexer { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureOpenai { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureLoadTesting { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureAgentIds { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureServiceGroups { get; set; } = [];

    // Monitoring / observability
    public Dictionary<string, AzureServiceResource> AzureMonitorComponents { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureAlerts { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureDiagnosticSettings { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureLogAnalyticsWorkspaces { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureMetrics { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureWorkbooks { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureActivityLogs { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureDatabaseWatchers { get; set; } = [];

    // Networking
    public Dictionary<string, AzureServiceResource> AzureNetworkWatchers { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureDnsZones { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureNetworkInterfaces { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzurePrivateDnsZones { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzurePublicIpAddresses { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureRouteTables { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureVirtualNetworks { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureLocalNetworkGateways { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzurePeeringServices { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzurePeerings { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureVirtualNetworkGateways { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureVirtualWans { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureDdosProtectionPlans { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureFirewalls { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureIpGroups { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureNetworkSecurityGroups { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureApplicationGateways { get; set; } = [];
    public Dictionary<string, AzureServiceResource> AzureApplicationSecurityGroups { get; set; } = [];

    private static readonly Dictionary<string, System.Reflection.PropertyInfo> PropByField =
        ResourceTypeRegistry.All.ToDictionary(
            r => r.FieldName,
            r => typeof(ResourcesConfig).GetProperty(r.PropertyName)!);

    private IDictionary DictFor(ResourceTypeInfo info) =>
        (IDictionary)PropByField[info.FieldName].GetValue(this)!;

    /// <summary>Get the resource object stored under a snake_case field name + key, or null.</summary>
    public object? GetResourceObject(string fieldName, string key)
    {
        if (!PropByField.TryGetValue(fieldName, out var prop))
        {
            return null;
        }
        var dict = (IDictionary)prop.GetValue(this)!;
        return dict.Contains(key) ? dict[key] : null;
    }

    /// <summary>Return all resource keys across all types.</summary>
    public HashSet<string> AllResourceKeys()
    {
        var keys = new HashSet<string>();
        foreach (var info in ResourceTypeRegistry.All)
        {
            foreach (var key in DictFor(info).Keys)
            {
                keys.Add((string)key);
            }
        }
        return keys;
    }

    /// <summary>Return the resource type field name for a given key.</summary>
    public string? GetResourceType(string key)
    {
        foreach (var info in ResourceTypeRegistry.All)
        {
            if (DictFor(info).Contains(key))
            {
                return info.FieldName;
            }
        }
        return null;
    }

    [GeneratedRegex("^[a-zA-Z0-9_]+$")]
    private static partial Regex StrictNamePattern();

    [GeneratedRegex("^[a-zA-Z0-9_ -]+$")]
    private static partial Regex GeneralNamePattern();

    [GeneratedRegex("^[a-z0-9]{3,24}$")]
    private static partial Regex StorageAccountNamePattern();

    /// <summary>
    /// Validate resource names follow Fabric naming rules per item type.
    /// Returns the list of warnings (same text as the Python implementation).
    /// </summary>
    public List<string> ValidateResourceNames()
    {
        var warnings = new List<string>();
        foreach (var info in ResourceTypeRegistry.All)
        {
            foreach (var keyObj in DictFor(info).Keys)
            {
                var key = (string)keyObj;
                if (key.Length > 256)
                {
                    warnings.Add($"'{key}' exceeds 256 character limit");
                }
                if (key != key.Trim())
                {
                    warnings.Add($"'{key}' has leading/trailing whitespace");
                }

                // Fabric item-name character rules don't apply to other platforms
                // (Entra display names allow apostrophes/periods; Azure names have
                // their own per-type rules that the ARM/Graph APIs enforce).
                if (info.Platform != ResourcePlatform.Fabric)
                {
                    if (info.ProviderType == "Microsoft.Storage/storageAccounts"
                        && !StorageAccountNamePattern().IsMatch(key))
                    {
                        warnings.Add(
                            $"'{key}' ({info.FieldName}): storage account names must be 3–24 characters, " +
                            "lowercase letters and numbers only.");
                    }
                    continue;
                }

                if (info.StrictNaming)
                {
                    if (!StrictNamePattern().IsMatch(key))
                    {
                        warnings.Add(
                            $"'{key}' ({info.FieldName}): only letters, numbers, and underscores allowed. " +
                            "Hyphens and spaces are not supported.");
                    }
                }
                else if (!GeneralNamePattern().IsMatch(key))
                {
                    warnings.Add($"'{key}' ({info.FieldName}): contains invalid characters");
                }
            }
        }
        return warnings;
    }
}
