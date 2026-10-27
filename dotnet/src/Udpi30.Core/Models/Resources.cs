namespace Udpi30.Core.Models;

// ---------------------------------------------------------------------------
// Shortcuts / tables
// ---------------------------------------------------------------------------

/// <summary>Shortcut transformation — auto-converts files to Delta tables.</summary>
public sealed class ShortcutTransformation
{
    public string Type { get; set; } = "file";
    public string? SourceFormat { get; set; }
    public string? DestinationTable { get; set; }
    public bool Sync { get; set; } = true;

    // AI-powered transformation options
    public string? AiSkill { get; set; }
    public string? AiModel { get; set; }
    public string? AiPrompt { get; set; }

    // File transformation options
    public bool Flatten { get; set; }
    public string? Compression { get; set; }
}

/// <summary>OneLake shortcut definition.</summary>
public sealed class ShortcutConfig
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Path { get; set; } = "Tables";
    public string? ConnectionId { get; set; }
    public ShortcutTransformation? Transformation { get; set; }
}

/// <summary>Delta table schema definition.</summary>
public sealed class TableSchema
{
    public string? SchemaPath { get; set; }
    public List<string> PartitionBy { get; set; } = [];
    public string? Description { get; set; }
}

// ---------------------------------------------------------------------------
// Resource definitions
// ---------------------------------------------------------------------------

public sealed class LakehouseResource
{
    public string? Description { get; set; }
    public List<string> Schemas { get; set; } = [];
    public List<ShortcutConfig> Shortcuts { get; set; } = [];
    public bool EnableSchemas { get; set; } = true;
    public bool SqlEndpointEnabled { get; set; } = true;
    public Dictionary<string, TableSchema> Tables { get; set; } = [];
}

/// <summary>
/// A Fabric Materialized Lake View — a managed, incrementally-refreshed view
/// over lakehouse Delta tables, defined by SQL. Bound to a parent lakehouse.
/// </summary>
public sealed class MaterializedLakeViewResource
{
    public string? Description { get; set; }

    /// <summary>Path to the SQL/definition file for the view.</summary>
    public string? Path { get; set; }

    /// <summary>The lakehouse the materialized view is created in.</summary>
    public string? DefaultLakehouse { get; set; }

    /// <summary>Optional refresh schedule (cron) for the materialized view.</summary>
    public string? RefreshCron { get; set; }

    public string? Folder { get; set; }
}

public sealed class NotebookResource
{
    public string Path { get; set; } = "";
    public string? Description { get; set; }
    public string? Environment { get; set; }
    public string? DefaultLakehouse { get; set; }
    public string? ExternalLakehouse { get; set; }
    public Dictionary<string, string> SparkProperties { get; set; } = [];
    public Dictionary<string, object?> Parameters { get; set; } = [];
    public string? Folder { get; set; }
}

public sealed class PipelineSchedule
{
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;
    public string? Cron { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string? StartTime { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class PipelineActivity
{
    public string? Name { get; set; }
    public string? Notebook { get; set; }
    public string? Pipeline { get; set; }
    public List<string> DependsOn { get; set; } = [];
    public Dictionary<string, object?> Parameters { get; set; } = [];
}

public sealed class PipelineResource
{
    public string? Path { get; set; }
    public string? Description { get; set; }
    public PipelineSchedule? Schedule { get; set; }
    public List<PipelineActivity> Activities { get; set; } = [];
    public string? Folder { get; set; }
}

public sealed class WarehouseResource
{
    public string? Description { get; set; }
    public List<string> SqlScripts { get; set; } = [];
    public string? Folder { get; set; }
}

public sealed class SemanticModelResource
{
    public string Path { get; set; } = "";
    public string? Description { get; set; }
    public string? DefaultLakehouse { get; set; }
    public bool AutoRefresh { get; set; }
    public int RefreshTimeout { get; set; } = 600;
    public List<string> AfterDeploy { get; set; } = [];
    public List<string> DependsOnRun { get; set; } = [];
    public string? Folder { get; set; }
}

public sealed class ReportResource
{
    public string Path { get; set; } = "";
    public string? Description { get; set; }
    public string? SemanticModel { get; set; }
    public string? ExternalSemanticModel { get; set; }
    public string? Folder { get; set; }
}

/// <summary>Data Agent grounding and configuration (resource key: <c>data_agents</c>).</summary>
public sealed class DataAgentInstructions
{
    public List<string> Sources { get; set; } = [];
    public string? Instructions { get; set; }
    public string? FewShotExamples { get; set; }
    public List<string> TablesInScope { get; set; } = [];
    public string? Description { get; set; }
}

public sealed class EnvironmentResource
{
    public string Runtime { get; set; } = "1.3";
    public List<string> Libraries { get; set; } = [];
    public List<string> CondaDependencies { get; set; } = [];
    public Dictionary<string, string> SparkProperties { get; set; } = [];
    public string? Description { get; set; }
}

public sealed class EventhouseResource
{
    public string? Description { get; set; }
    public List<string> KqlScripts { get; set; } = [];
    public int? RetentionDays { get; set; }
    public int? CacheDays { get; set; }
}

public sealed class EventstreamResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public List<Dictionary<string, object?>> Sources { get; set; } = [];
    public List<Dictionary<string, object?>> Destinations { get; set; } = [];
}

public sealed class MLModelResource
{
    public string? Path { get; set; }
    public string? Description { get; set; }
    public string? Framework { get; set; }
}

public sealed class MLExperimentResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}

public sealed class KQLDatabaseResource
{
    public string? Description { get; set; }
    public string? ParentEventhouse { get; set; }
    public List<string> KqlScripts { get; set; } = [];
}

public sealed class KQLDashboardResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? DataSource { get; set; }
}

public sealed class KQLQuerysetResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? DataSource { get; set; }
}

public sealed class DataflowResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}

public sealed class GraphQLApiResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? DataSource { get; set; }
}

public sealed class SparkJobDefinitionResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? Environment { get; set; }
    public string? DefaultLakehouse { get; set; }
    public List<string> Args { get; set; } = [];
    public Dictionary<string, string> Conf { get; set; } = [];
}

public sealed class SQLDatabaseResource
{
    public string? Description { get; set; }
    public List<string> SqlScripts { get; set; } = [];
}

public sealed class MirroredDatabaseResource
{
    public string? Description { get; set; }
    public string? SourceType { get; set; }
    public string? Connection { get; set; }
}

public sealed class CopyJobResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}

public sealed class ApacheAirflowJobResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}

public sealed class ReflexResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}

public sealed class MountedDataFactoryResource
{
    public string? Description { get; set; }
    public string? DataFactoryId { get; set; }
}

public sealed class UserDataFunctionResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? Runtime { get; set; }
}

public sealed class VariableLibraryResource
{
    public string? Description { get; set; }
    public Dictionary<string, string> Variables { get; set; } = [];
}

public sealed class OntologyResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public List<string> DataSources { get; set; } = [];
}

public sealed class GraphResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? DataSource { get; set; }
}

public sealed class DataBuildToolJobResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? Environment { get; set; }
}

public sealed class DatamartResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}

public sealed class PaginatedReportResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? DataSource { get; set; }
}

public sealed class DashboardResource
{
    public string? Description { get; set; }
}

public sealed class MirroredWarehouseResource
{
    public string? Description { get; set; }
    public string? SourceType { get; set; }
}

public sealed class SnowflakeDatabaseResource
{
    public string? Description { get; set; }
    public string? Connection { get; set; }
}

public sealed class CosmosDBDatabaseResource
{
    public string? Description { get; set; }
    public string? Connection { get; set; }
}

public sealed class MirroredDatabricksCatalogResource
{
    public string? Description { get; set; }
    public string? Connection { get; set; }
}

public sealed class OperationsAgentResource
{
    public string? Description { get; set; }
    public List<string> Sources { get; set; } = [];
    public string? Instructions { get; set; }
}

public sealed class AnomalyDetectorResource
{
    public string? Description { get; set; }
    public string? DataSource { get; set; }
    public string? Path { get; set; }
}

public sealed class DigitalTwinBuilderResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}

public sealed class DigitalTwinBuilderFlowResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? TwinBuilder { get; set; }
}

public sealed class EventSchemaSetResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}

public sealed class GraphQuerySetResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? DataSource { get; set; }
}

public sealed class MapResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}

public sealed class GraphModelResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? DataSource { get; set; }
}

public sealed class HLSCohortResource
{
    public string? Description { get; set; }
    public string? Path { get; set; }
}
