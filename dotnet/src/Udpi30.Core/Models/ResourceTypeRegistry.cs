namespace Udpi30.Core.Models;

/// <summary>
/// The control plane a resource type is deployed through. Each platform has its
/// own provider (client, auth scope, create/delete semantics). Defaults to
/// <see cref="Fabric"/> so existing Fabric rows need no extra argument.
/// </summary>
public enum ResourcePlatform
{
    /// <summary>Microsoft Fabric items, deployed via the Fabric REST API into a workspace.</summary>
    Fabric,

    /// <summary>Azure ARM resources, deployed via Bicep/ARM deployments at subscription or resource-group scope.</summary>
    Azure,

    /// <summary>Microsoft Entra (Azure AD) directory objects, deployed via Microsoft Graph at tenant scope.</summary>
    Entra,
}

/// <summary>
/// Metadata for a single resource type. <see cref="FieldName"/> is the
/// snake_case key used in <c>udp.yml</c> (and reported by diagnostics);
/// <see cref="PropertyName"/> is the corresponding <see cref="ResourcesConfig"/>
/// property; <see cref="ProviderType"/> is the platform-specific type identifier
/// (a Fabric item-type name, an ARM resource type, or a Graph entity type);
/// <see cref="Platform"/> selects the provider that deploys it; <see cref="Folder"/>
/// is the workspace folder the type is grouped under when
/// <c>workspace.folders_by_type</c> is enabled (Fabric-only).
/// </summary>
/// <remarks>
/// <see cref="Folder"/> defaults to <c>"Other"</c>, so a newly added resource
/// type is always assigned a folder even if the author forgets to pick one —
/// no type can ever be left unfoldered. <see cref="Platform"/> defaults to
/// <see cref="ResourcePlatform.Fabric"/> so the 45 original Fabric rows are
/// unchanged.
/// </remarks>
public sealed record ResourceTypeInfo(
    string FieldName,
    string PropertyName,
    string ProviderType,
    bool StrictNaming,
    ResourcePlatform Platform = ResourcePlatform.Fabric,
    string Folder = "Other")
{
    /// <summary>
    /// Back-compat alias for <see cref="ProviderType"/>. Only meaningful when
    /// <see cref="Platform"/> is <see cref="ResourcePlatform.Fabric"/>, where it
    /// is the Microsoft Fabric item-type name.
    /// </summary>
    public string FabricType => ProviderType;
}

/// <summary>
/// Single source of truth for the 45 supported resource types. Centralizes the
/// list that the Python code duplicated across the models, the resolver/planner,
/// and <c>ITEM_TYPE_MAP</c> in the Fabric API provider.
/// </summary>
public static class ResourceTypeRegistry
{
    // Strict-naming types: only letters, numbers and underscores allowed
    // (mirrors strict_name_types in ResourcesConfig.validate_resource_names).
    // Folder: the workspace folder each type is grouped under (folders_by_type).
    public static readonly IReadOnlyList<ResourceTypeInfo> All =
    [
        new("lakehouses", "Lakehouses", "Lakehouse", StrictNaming: true, Folder: "Lakehouses"),
        new("notebooks", "Notebooks", "Notebook", false, Folder: "Notebooks"),
        new("pipelines", "Pipelines", "DataPipeline", false, Folder: "Pipelines"),
        new("warehouses", "Warehouses", "Warehouse", StrictNaming: true, Folder: "Warehouses"),
        new("semantic_models", "SemanticModels", "SemanticModel", false, Folder: "Models"),
        new("reports", "Reports", "Report", false, Folder: "Reports"),
        new("data_agents", "DataAgents", "DataAgent", false, Folder: "Agents"),
        new("environments", "Environments", "Environment", false, Folder: "Environments"),
        new("eventhouses", "Eventhouses", "Eventhouse", StrictNaming: true, Folder: "Real-Time"),
        new("eventstreams", "Eventstreams", "Eventstream", false, Folder: "Real-Time"),
        new("ml_models", "MlModels", "MLModel", false, Folder: "Models"),
        new("ml_experiments", "MlExperiments", "MLExperiment", false, Folder: "Models"),
        new("kql_databases", "KqlDatabases", "KQLDatabase", StrictNaming: true, Folder: "Databases"),
        new("kql_dashboards", "KqlDashboards", "KQLDashboard", false, Folder: "Real-Time"),
        new("kql_querysets", "KqlQuerysets", "KQLQueryset", false, Folder: "Real-Time"),
        new("dataflows", "Dataflows", "Dataflow", false, Folder: "Data Factory"),
        new("graphql_apis", "GraphqlApis", "GraphQLApi", false, Folder: "Data Engineering"),
        new("spark_job_definitions", "SparkJobDefinitions", "SparkJobDefinition", false, Folder: "Data Engineering"),
        new("sql_databases", "SqlDatabases", "SQLDatabase", StrictNaming: true, Folder: "Databases"),
        new("mirrored_databases", "MirroredDatabases", "MirroredDatabase", false, Folder: "Databases"),
        new("copy_jobs", "CopyJobs", "CopyJob", false, Folder: "Data Factory"),
        new("airflow_jobs", "AirflowJobs", "ApacheAirflowJob", false, Folder: "Data Factory"),
        new("reflex", "Reflex", "Reflex", false, Folder: "Real-Time"),
        new("mounted_data_factories", "MountedDataFactories", "MountedDataFactory", false, Folder: "Data Factory"),
        new("user_data_functions", "UserDataFunctions", "UserDataFunction", false, Folder: "Data Engineering"),
        new("variable_libraries", "VariableLibraries", "VariableLibrary", false, Folder: "Variables"),
        new("ontologies", "Ontologies", "Ontology", false, Folder: "Real-Time"),
        new("graphs", "Graphs", "Graph", false, Folder: "Graph"),
        new("dbt_jobs", "DbtJobs", "DataBuildToolJob", false, Folder: "Data Factory"),
        new("datamarts", "Datamarts", "Datamart", false, Folder: "Databases"),
        new("paginated_reports", "PaginatedReports", "PaginatedReport", false, Folder: "Reports"),
        new("dashboards", "Dashboards", "Dashboard", false, Folder: "Reports"),
        new("mirrored_warehouses", "MirroredWarehouses", "MirroredWarehouse", false, Folder: "Databases"),
        new("snowflake_databases", "SnowflakeDatabases", "SnowflakeDatabase", false, Folder: "Databases"),
        new("cosmosdb_databases", "CosmosdbDatabases", "CosmosDBDatabase", false, Folder: "Databases"),
        new("mirrored_databricks_catalogs", "MirroredDatabricksCatalogs", "MirroredAzureDatabricksCatalog", false, Folder: "Databases"),
        new("operations_agents", "OperationsAgents", "OperationsAgent", false, Folder: "Agents"),
        new("anomaly_detectors", "AnomalyDetectors", "AnomalyDetector", false, Folder: "Real-Time"),
        new("digital_twin_builders", "DigitalTwinBuilders", "DigitalTwinBuilder", false, Folder: "Real-Time"),
        new("digital_twin_builder_flows", "DigitalTwinBuilderFlows", "DigitalTwinBuilderFlow", false, Folder: "Real-Time"),
        new("event_schema_sets", "EventSchemaSets", "EventSchemaSet", false, Folder: "Real-Time"),
        new("graph_query_sets", "GraphQuerySets", "GraphQuerySet", false, Folder: "Graph"),
        new("map_items", "MapItems", "Map", false, Folder: "Maps"),
        new("graph_models", "GraphModels", "GraphModel", false, Folder: "Graph"),
        new("hls_cohorts", "HlsCohorts", "HLSCohort", false, Folder: "Healthcare"),
        new("materialized_lake_views", "MaterializedLakeViews", "MaterializedLakeView", false, Folder: "Data Engineering"),

        // --- Microsoft Entra (Graph, tenant scope). No Fabric workspace/folder. ---
        new("entra_groups", "EntraGroups", "group", StrictNaming: false, Platform: ResourcePlatform.Entra),
        new("entra_apps", "EntraApps", "application", StrictNaming: false, Platform: ResourcePlatform.Entra),

        // --- Azure (ARM via Bicep). No Fabric workspace/folder. ---
        new("azure_resource_groups", "AzureResourceGroups", "Microsoft.Resources/resourceGroups", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_storage_accounts", "AzureStorageAccounts", "Microsoft.Storage/storageAccounts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_deployments", "AzureDeployments", "Microsoft.Resources/deployments", StrictNaming: false, Platform: ResourcePlatform.Azure),

        // --- Azure service resources (generic single-resource Bicep). ---
        // Integration / streaming / compute
        new("azure_data_factories", "AzureDataFactories", "Microsoft.DataFactory/factories", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_databricks_workspaces", "AzureDatabricksWorkspaces", "Microsoft.Databricks/workspaces", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_databricks_structured_streaming", "AzureDatabricksStructuredStreaming", "Microsoft.Databricks/workspaces", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_event_hub_namespaces", "AzureEventHubNamespaces", "Microsoft.EventHub/namespaces", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_event_grid_topics", "AzureEventGridTopics", "Microsoft.EventGrid/topics", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_stream_analytics_jobs", "AzureStreamAnalyticsJobs", "Microsoft.StreamAnalytics/streamingjobs", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_iot_hubs", "AzureIotHubs", "Microsoft.Devices/IotHubs", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_logic_apps", "AzureLogicApps", "Microsoft.Logic/workflows", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_functions", "AzureFunctions", "Microsoft.Web/sites", StrictNaming: false, Platform: ResourcePlatform.Azure),

        // Storage family (all deploy as a storage account with service-specific kind/properties)
        new("azure_blob_storage", "AzureBlobStorage", "Microsoft.Storage/storageAccounts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_data_lake_storage", "AzureDataLakeStorage", "Microsoft.Storage/storageAccounts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_files", "AzureFiles", "Microsoft.Storage/storageAccounts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_queue_storage", "AzureQueueStorage", "Microsoft.Storage/storageAccounts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_table_storage", "AzureTableStorage", "Microsoft.Storage/storageAccounts", StrictNaming: false, Platform: ResourcePlatform.Azure),

        // Databases
        new("azure_sql_databases", "AzureSqlDatabases", "Microsoft.Sql/servers/databases", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_sql_managed_instances", "AzureSqlManagedInstances", "Microsoft.Sql/managedInstances", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_sql_virtual_machines", "AzureSqlVirtualMachines", "Microsoft.SqlVirtualMachine/sqlVirtualMachines", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_postgresql", "AzurePostgresql", "Microsoft.DBforPostgreSQL/flexibleServers", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_mysql", "AzureMysql", "Microsoft.DBforMySQL/flexibleServers", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_mariadb", "AzureMariadb", "Microsoft.DBforMariaDB/servers", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_cosmosdb_accounts", "AzureCosmosdbAccounts", "Microsoft.DocumentDB/databaseAccounts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_redis_cache", "AzureRedisCache", "Microsoft.Cache/Redis", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_data_box", "AzureDataBox", "Microsoft.DataBox/jobs", StrictNaming: false, Platform: ResourcePlatform.Azure),

        // Governance / security / AI
        new("azure_purview_accounts", "AzurePurviewAccounts", "Microsoft.Purview/accounts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_key_vaults", "AzureKeyVaults", "Microsoft.KeyVault/vaults", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_policy_assignments", "AzurePolicyAssignments", "Microsoft.Authorization/policyAssignments", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_defender_plans", "AzureDefenderPlans", "Microsoft.Security/pricings", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_machine_learning_workspaces", "AzureMachineLearningWorkspaces", "Microsoft.MachineLearningServices/workspaces", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_ai_foundry", "AzureAiFoundry", "Microsoft.MachineLearningServices/workspaces", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_sentinel", "AzureSentinel", "Microsoft.SecurityInsights/onboardingStates", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_video_indexer", "AzureVideoIndexer", "Microsoft.VideoIndexer/accounts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_openai", "AzureOpenai", "Microsoft.CognitiveServices/accounts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_load_testing", "AzureLoadTesting", "Microsoft.LoadTestService/loadTests", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_agent_ids", "AzureAgentIds", "Microsoft.ManagedIdentity/userAssignedIdentities", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_service_groups", "AzureServiceGroups", "Microsoft.Management/serviceGroups", StrictNaming: false, Platform: ResourcePlatform.Azure),

        // Monitoring / observability
        new("azure_monitor_components", "AzureMonitorComponents", "Microsoft.Insights/components", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_alerts", "AzureAlerts", "Microsoft.Insights/metricAlerts", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_diagnostic_settings", "AzureDiagnosticSettings", "Microsoft.Insights/diagnosticSettings", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_log_analytics_workspaces", "AzureLogAnalyticsWorkspaces", "Microsoft.OperationalInsights/workspaces", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_metrics", "AzureMetrics", "Microsoft.Insights/dataCollectionRules", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_workbooks", "AzureWorkbooks", "Microsoft.Insights/workbooks", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_activity_logs", "AzureActivityLogs", "Microsoft.Insights/diagnosticSettings", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_database_watchers", "AzureDatabaseWatchers", "Microsoft.DatabaseWatcher/watchers", StrictNaming: false, Platform: ResourcePlatform.Azure),

        // Networking
        new("azure_network_watchers", "AzureNetworkWatchers", "Microsoft.Network/networkWatchers", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_dns_zones", "AzureDnsZones", "Microsoft.Network/dnsZones", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_network_interfaces", "AzureNetworkInterfaces", "Microsoft.Network/networkInterfaces", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_private_dns_zones", "AzurePrivateDnsZones", "Microsoft.Network/privateDnsZones", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_public_ip_addresses", "AzurePublicIpAddresses", "Microsoft.Network/publicIPAddresses", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_route_tables", "AzureRouteTables", "Microsoft.Network/routeTables", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_virtual_networks", "AzureVirtualNetworks", "Microsoft.Network/virtualNetworks", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_local_network_gateways", "AzureLocalNetworkGateways", "Microsoft.Network/localNetworkGateways", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_peering_services", "AzurePeeringServices", "Microsoft.Network/peeringServices", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_peerings", "AzurePeerings", "Microsoft.Peering/peerings", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_virtual_network_gateways", "AzureVirtualNetworkGateways", "Microsoft.Network/virtualNetworkGateways", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_virtual_wans", "AzureVirtualWans", "Microsoft.Network/virtualWans", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_ddos_protection_plans", "AzureDdosProtectionPlans", "Microsoft.Network/ddosProtectionPlans", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_firewalls", "AzureFirewalls", "Microsoft.Network/azureFirewalls", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_ip_groups", "AzureIpGroups", "Microsoft.Network/ipGroups", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_network_security_groups", "AzureNetworkSecurityGroups", "Microsoft.Network/networkSecurityGroups", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_application_gateways", "AzureApplicationGateways", "Microsoft.Network/applicationGateways", StrictNaming: false, Platform: ResourcePlatform.Azure),
        new("azure_application_security_groups", "AzureApplicationSecurityGroups", "Microsoft.Network/applicationSecurityGroups", StrictNaming: false, Platform: ResourcePlatform.Azure),
    ];

    /// <summary>Resource types for a single platform (provider).</summary>
    public static IEnumerable<ResourceTypeInfo> ForPlatform(ResourcePlatform platform) =>
        All.Where(r => r.Platform == platform);

    /// <summary>Lookup of resource metadata by its snake_case field name (globally unique).</summary>
    public static readonly IReadOnlyDictionary<string, ResourceTypeInfo> ByField =
        All.ToDictionary(r => r.FieldName);

    /// <summary>
    /// The platform that owns a snake_case field name. Falls back to
    /// <see cref="ResourcePlatform.Fabric"/> for an unknown field name, matching
    /// the historical assumption that every type is a Fabric item.
    /// </summary>
    public static ResourcePlatform PlatformFor(string fieldName) =>
        ByField.TryGetValue(fieldName, out var info) ? info.Platform : ResourcePlatform.Fabric;

    /// <summary>
    /// Platform-native type identifier for a snake_case resource field name
    /// (Fabric item type, ARM type, or Graph entity type). Keyed by field name,
    /// which is globally unique across platforms, so the planner can stamp the
    /// correct <see cref="ResourceTypeInfo.ProviderType"/> on every plan item
    /// regardless of platform. (The <em>reverse</em> map in ReverseGenerator is
    /// Fabric-scoped, since item-type names are only unique within Fabric.)
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ItemTypeMap =
        All.ToDictionary(r => r.FieldName, r => r.ProviderType);

    /// <summary>
    /// Workspace folder name each resource type is grouped under when
    /// <c>workspace.folders_by_type</c> is enabled, keyed by snake_case field
    /// name. Derived from <see cref="ResourceTypeInfo.Folder"/>, so every type —
    /// including any added later — is always present.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TypeFolders =
        All.ToDictionary(r => r.FieldName, r => r.Folder);

    /// <summary>The per-type workspace folder for a snake_case field name.
    /// Falls back to <c>"Other"</c> for an unknown field name.</summary>
    public static string FolderFor(string fieldName) => TypeFolders.GetValueOrDefault(fieldName, "Other");

    /// <summary>Item types that are list-only — cannot be created/deleted via API.</summary>
    public static readonly IReadOnlySet<string> ListOnlyTypes = new HashSet<string>
    {
        "Datamart", "MirroredWarehouse", "SQLEndpoint", "Dashboard", "PaginatedReport",
    };

    /// <summary>Item types that REQUIRE a definition to create (cannot create empty).</summary>
    public static readonly IReadOnlySet<string> DefinitionRequiredTypes = new HashSet<string>
    {
        "MountedDataFactory", "MirroredDatabase", "Report", "SemanticModel",
    };

    /// <summary>Item types where definition upload is not supported.</summary>
    public static readonly IReadOnlySet<string> NoDefinitionTypes = new HashSet<string>
    {
        "MLModel", "MLExperiment", "Warehouse",
    };
}
