using Udpi30.Core.Yaml;

namespace Udpi30.Core.Models;

public enum WorkspaceRole
{
    [EnumValue("admin")] Admin,
    [EnumValue("member")] Member,
    [EnumValue("contributor")] Contributor,
    [EnumValue("viewer")] Viewer,
}

public enum OneLakePermission
{
    [EnumValue("read")] Read,
    [EnumValue("write")] Write,
    [EnumValue("readwrite")] ReadWrite,
}

public enum ConnectionType
{
    [EnumValue("adls_gen2")] AdlsGen2,
    [EnumValue("sql_server")] SqlServer,
    [EnumValue("azure_sql")] AzureSql,
    [EnumValue("cosmos_db")] CosmosDb,
    [EnumValue("kusto")] Kusto,
    [EnumValue("http")] Http,
    [EnumValue("custom")] Custom,
}

public enum ScheduleFrequency
{
    [EnumValue("once")] Once,
    [EnumValue("hourly")] Hourly,
    [EnumValue("daily")] Daily,
    [EnumValue("weekly")] Weekly,
    [EnumValue("monthly")] Monthly,
    [EnumValue("cron")] Cron,
}

public enum SparkRuntimeVersion
{
    [EnumValue("1.2")] V1_2,
    [EnumValue("1.3")] V1_3,
}

public enum DeployAction
{
    [EnumValue("create")] Create,
    [EnumValue("update")] Update,
    [EnumValue("delete")] Delete,
    [EnumValue("no_change")] NoChange,
}
