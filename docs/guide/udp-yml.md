# udp.yml reference

This topic is the complete reference for the `udp.yml` schema: every top-level key, field, type, default value, and validation rule. The `udp.yml` file is the single declarative definition for a Microsoft Fabric project, covering every resource, environment target, security role, connection, and policy in one file (or split across multiple files using `include`).

`udp.yml` describes the **desired state** of your workspace: *what* you want, not *how* to build it. The CLI handles dependency ordering, diffing against actual state, and applying the changes. For the conceptual model behind that loop, see [The declarative model](declarative-model.md).

---

## 1. File structure overview

```yaml
deployment:          # Required. Project metadata.
workspace:       # Default workspace configuration.
variables:       # Variable definitions with optional defaults.
resources:       # Resource definitions — Fabric (46 types), Entra, and Azure.
azure:           # Azure deployment defaults (subscription, location).
security:        # Workspace and OneLake role assignments.
connections:     # Data source connection definitions.
policies:        # Validation and governance rules.
notifications:   # Deployment notification hooks.
state:           # Remote state backend configuration.
admin:           # Tenant-level (admin) settings, applied tenant-wide.
targets:         # Environment-specific overrides (dev, staging, prod).
include:         # Additional YAML files to merge into the deployment.
extends:         # Parent deployment to inherit from.
```

> **Note**
>
> Only the `deployment` key (with its `name` field) is required. All other top-level keys are optional.

---

## 2. Schema validation and parsing

udpi30 parses `udp.yml` with YamlDotNet, constructed through the `YamlFactory`. The parsed document is bound to C# model classes in `Udpi30.Core.Models`, including `DeploymentDefinition`, `ResourcesConfig`, `TargetConfig`, `WorkspaceConfig`, and `StateConfig`.

A JSON Schema file, `udp.schema.json`, ships at the repository root for editor IntelliSense, autocompletion, and validation:

```yaml
# yaml-language-server: $schema=../../udp.schema.json
deployment:
  name: udp-project
```

---

## 3. deployment

Project metadata and identity. This is the only required top-level key.

### 3.1 Fields

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | String | **Yes** | | Unique identifier for the deployment. Used in state files, deployment history, and destroy confirmations. |
| `version` | String | No | `"0.1.0"` | Semantic version string. Recorded in deployment state and history. |
| `description` | String | No | | Human-readable description of the project. |
| `depends_on` | List of strings | No | `[]` | Paths to other `udp.yml` files that this deployment depends on. Used for cross-deployment dependency resolution. |

### 3.2 Example

```yaml
deployment:
  name: sales-analytics
  version: "2.1.0"
  description: "End-to-end sales analytics pipeline with medallion architecture"
  depends_on:
    - ../shared-infrastructure/udp.yml
    - ../data-platform/udp.yml
```

> **Important**
>
> The `name` field is used as a confirmation prompt during `udpi30 destroy`. Choose a descriptive, unique name. Changing the name after initial deployment creates a new state track.

---

## 4. workspace

Default workspace configuration. Targets can override these values.

### 4.1 Fields

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | String | No | | Workspace display name. If the workspace does not exist, `deploy` creates it. |
| `workspace_id` | String | No | | GUID of an existing workspace to deploy into. If set, `name` is used for display only. |
| `capacity_id` | String | No | | Fabric capacity GUID. Required when creating a new workspace. Must be a valid GUID format (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`) or a variable reference (`${var.capacity_id}`). |
| `capacity` | String | No | | **Deprecated.** Use `capacity_id` instead. |
| `description` | String | No | | Workspace description. |
| `folders_by_type` | Boolean | No | `false` | When `true`, `deploy` organizes items into Fabric workspace folders by type (`Notebooks`, `Pipelines`, `Lakehouses`, `Reports`, `Models`, `Databases`, `Warehouses`, `Environments`, `Agents`, `Variables`). A per-resource `folder` always overrides the type folder. Folders are created on item creation; existing items are not moved. |
| `git_integration` | Object | No | | Git integration settings for the workspace. See [git_integration](#43-git_integration). |

### 4.2 Example

```yaml
workspace:
  name: sales-analytics-dev
  capacity_id: "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
  description: "Development workspace for the sales analytics team"
  git_integration:
    provider: github
    organization: udp-org
    repository: sales-analytics
    branch: main
    directory: /
```

> **Warning**
>
> If you provide both `workspace_id` and `name`, the `workspace_id` takes precedence. The tool deploys to the workspace identified by the GUID, regardless of the `name` value.

### 4.3 git_integration

Git integration settings that connect the workspace to a source control repository.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `provider` | String | No | `"azuredevops"` | Git provider: `azuredevops` or `github`. |
| `organization` | String | No | | GitHub organization or Azure DevOps organization name. |
| `project` | String | No | | Azure DevOps project name. Not used for GitHub. |
| `repository` | String | No | | Repository name. |
| `branch` | String | No | `"main"` | Branch to sync with. |
| `directory` | String | No | `"/"` | Root directory in the repository for Fabric items. |

```yaml
workspace:
  git_integration:
    provider: azuredevops
    organization: contoso
    project: data-platform
    repository: udp-items
    branch: main
    directory: /workspace
```

### 4.4 Workspace folders by type

Set `folders_by_type: true` to have `deploy` organize items into Fabric
workspace folders grouped by type. The folder is chosen automatically per
resource type; a per-resource `folder:` always takes precedence.

```yaml
workspace:
  capacity_id: "${var.capacity_id}"
  folders_by_type: true
```

**Every** resource type is assigned a folder — including any type added in a
future release, which falls back to `Other` until it is given a category — so no
item is ever left ungrouped. The mapping:

| Folder | Resource types |
|---|---|
| `Lakehouses` | `lakehouses` |
| `Notebooks` | `notebooks` |
| `Pipelines` | `pipelines` |
| `Warehouses` | `warehouses` |
| `Databases` | `sql_databases`, `kql_databases`, `mirrored_databases`, `snowflake_databases`, `cosmosdb_databases`, `mirrored_databricks_catalogs`, `mirrored_warehouses`, `datamarts` |
| `Models` | `semantic_models`, `ml_models`, `ml_experiments` |
| `Reports` | `reports`, `paginated_reports`, `dashboards` |
| `Agents` | `data_agents`, `operations_agents` |
| `Environments` | `environments` |
| `Variables` | `variable_libraries` |
| `Data Engineering` | `graphql_apis`, `spark_job_definitions`, `user_data_functions` |
| `Data Factory` | `dataflows`, `copy_jobs`, `airflow_jobs`, `mounted_data_factories`, `dbt_jobs` |
| `Real-Time` | `eventhouses`, `eventstreams`, `kql_dashboards`, `kql_querysets`, `reflex`, `anomaly_detectors`, `digital_twin_builders`, `digital_twin_builder_flows`, `event_schema_sets`, `ontologies` |
| `Graph` | `graphs`, `graph_models`, `graph_query_sets` |
| `Maps` | `map_items` |
| `Healthcare` | `hls_cohorts` |

To override the folder for a single item, set `folder:` on that resource (the
`folder` field is available on notebooks, pipelines, warehouses, semantic models
and reports):

```yaml
resources:
  notebooks:
    ad_hoc_analysis:
      path: ./notebooks/ad_hoc.py
      folder: "Scratch"   # overrides the default "Notebooks" folder
```

!!! note
    Folders are created/assigned when an item is **created**. Existing items are
    not moved into folders on a subsequent `update`. `folders_by_type` can also
    be set per [target](targets-variables.md).

---

## 5. variables

Variable definitions with optional descriptions and default values. Variables can be referenced anywhere in the deployment using `${var.variable_name}` syntax. Substitution is resolved recursively across the manifest by the `SecretsResolver`.

### 5.1 Field formats

Variables support two definition formats.

**Short form** (string value as default):

```yaml
variables:
  environment: "development"
  region: "eastus2"
```

**Long form** (with description and optional default):

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `description` | String | No | | Human-readable description of the variable. |
| `default` | String | No | | Default value. If no default is set and no target override provides a value, `--strict` validation fails. |

```yaml
variables:
  source_connection:
    description: "Connection string for the source database"
    default: "Server=dev-server;Database=sales"
  capacity_id:
    description: "Fabric capacity GUID"
  environment:
    description: "Deployment environment name"
    default: "dev"
```

### 5.2 Variable resolution order

Variables are resolved in the following order (highest priority first):

1. Target-specific `variables` overrides.
2. Variable `default` value from the top-level `variables` section.
3. Environment variables (for `${env.VAR_NAME}` and `${secret.SECRET_NAME}` syntax).
4. Azure Key Vault secrets (for `${keyvault.VAULT_NAME.SECRET_NAME}` syntax).

The built-in variables `${deployment.name}` and `${deployment.version}` are always available.

### 5.3 Examples

**Referencing variables in the deployment:**

```yaml
variables:
  lakehouse_name:
    description: "Name of the primary lakehouse"
    default: "bronze_lake"

resources:
  lakehouses:
    ${var.lakehouse_name}:
      description: "Primary ingestion lakehouse"
```

**Using secrets:**

```yaml
notifications:
  on_success:
    - type: slack
      webhook: "${secret.SLACK_WEBHOOK_URL}"
      message: "Deployed ${deployment.name} v${deployment.version}"
```

> **Important**
>
> `${secret.*}` values are read from environment variables at deploy time and are redacted in plan output and logs. For centrally managed secrets, use `${keyvault.VAULT_NAME.SECRET_NAME}`, which retrieves the value from Azure Key Vault. See [Secrets Management](secrets.md).

---

## 6. resources

All Fabric resource definitions organized by type. Each resource type is a dictionary where keys are the resource display names and values define the resource configuration.

### 6.1 Supported resource types (46 types)

The following table lists every supported resource type. Click a type name for details in the [resource type reference](resource-types.md).

| Category | Resource type key | Description |
|---|---|---|
| **Data Engineering** | `lakehouses` | Fabric Lakehouse with optional shortcuts and Delta table definitions. |
| | `notebooks` | Spark notebooks (.py, .ipynb). |
| | `environments` | Spark runtime environments with library dependencies. |
| | `spark_job_definitions` | Spark Job Definition resources (.py, .jar). |
| | `materialized_lake_views` | Materialized Lake Views (SQL views over lakehouse Delta tables). |
| | `pipelines` | Data Pipelines with activities and schedules. |
| | `dataflows` | Dataflow Gen2 definitions. |
| | `copy_jobs` | Copy Job resources. |
| | `airflow_jobs` | Apache Airflow Job (DAG) resources. |
| | `dbt_jobs` | dbt job resources. |
| **Data Warehousing** | `warehouses` | Fabric Warehouse with SQL scripts. |
| | `sql_databases` | SQL Database resources. |
| **Real-Time Intelligence** | `eventhouses` | Eventhouse (KQL database cluster) resources. |
| | `eventstreams` | Eventstream resources. |
| | `kql_databases` | KQL Database resources. |
| | `kql_dashboards` | KQL Dashboard resources. |
| | `kql_querysets` | KQL Queryset resources. |
| | `event_schema_sets` | Event Schema Set resources. |
| **Business Intelligence** | `semantic_models` | Semantic models (Power BI datasets). |
| | `reports` | Power BI reports (.pbir). |
| | `dashboards` | Power BI Dashboard resources. |
| | `paginated_reports` | Paginated reports (.rdl). |
| | `datamarts` | Datamart resources. |
| **AI & Machine Learning** | `data_agents` | Data Agent (AI/NL2SQL) resources. |
| | `ml_models` | ML Model resources. |
| | `ml_experiments` | ML Experiment resources. |
| | `operations_agents` | Operations Agent resources. |
| | `anomaly_detectors` | Anomaly Detector resources. |
| **Data Integration** | `mirrored_databases` | Mirrored Database resources. |
| | `mirrored_warehouses` | Mirrored Warehouse resources. |
| | `snowflake_databases` | Snowflake Database resources. |
| | `cosmosdb_databases` | Cosmos DB Database resources. |
| | `mirrored_databricks_catalogs` | Mirrored Azure Databricks Catalog resources. |
| | `mounted_data_factories` | Mounted Azure Data Factory resources. |
| **Functions & APIs** | `graphql_apis` | GraphQL API resources. |
| | `user_data_functions` | User Data Function resources. |
| **Governance & Management** | `variable_libraries` | Variable Library resources. |
| | `reflex` | Reflex (Data Activator) resources. |
| **Knowledge & Graphs** | `ontologies` | Fabric Ontology (knowledge graph) resources. |
| | `graphs` | Fabric Graph resources. |
| | `graph_query_sets` | Graph Query Set resources. |
| | `graph_models` | Graph Model resources. |
| | `map_items` | Map resources. |
| **IoT & Digital Twin** | `digital_twin_builders` | Digital Twin Builder resources. |
| | `digital_twin_builder_flows` | Digital Twin Builder Flow resources. |
| **Healthcare** | `hls_cohorts` | HLS Cohort (Healthcare) resources. |

In addition to the 46 Fabric item types, `resources` accepts **Entra** and
**Azure** types, deployed through Microsoft Graph and Bicep respectively:

| Platform | Resource type key | Description |
|---|---|---|
| **Entra** | `entra_groups` | Microsoft Entra security groups. |
| | `entra_apps` | Entra app registrations (+ optional service principal). |
| **Azure** | `azure_resource_groups` | Azure resource groups (subscription-scope Bicep). |
| | `azure_storage_accounts` | Azure Storage accounts (resource-group-scope Bicep). |
| | `azure_deployments` | Generic Bicep deployments (any ARM resource). |
| **Azure services** | `azure_data_factories`, `azure_databricks_workspaces`, `azure_databricks_structured_streaming`, `azure_event_hub_namespaces`, `azure_event_grid_topics`, `azure_stream_analytics_jobs`, `azure_iot_hubs`, `azure_logic_apps`, `azure_functions` | Integration / streaming / compute. |
| | `azure_blob_storage`, `azure_data_lake_storage`, `azure_files`, `azure_queue_storage`, `azure_table_storage` | Storage family (deploy as storage accounts). |
| | `azure_sql_databases`, `azure_sql_managed_instances`, `azure_sql_virtual_machines`, `azure_postgresql`, `azure_mysql`, `azure_mariadb`, `azure_cosmosdb_accounts`, `azure_redis_cache`, `azure_data_box` | Databases & data movement. |
| | `azure_purview_accounts`, `azure_key_vaults`, `azure_policy_assignments`, `azure_defender_plans`, `azure_machine_learning_workspaces`, `azure_ai_foundry`, `azure_sentinel`, `azure_video_indexer`, `azure_openai`, `azure_load_testing`, `azure_agent_ids`, `azure_service_groups` | Governance / security / AI. |
| | `azure_monitor_components`, `azure_alerts`, `azure_diagnostic_settings`, `azure_log_analytics_workspaces`, `azure_metrics`, `azure_workbooks`, `azure_activity_logs`, `azure_database_watchers` | Monitoring / observability. |
| | `azure_network_watchers`, `azure_dns_zones`, `azure_network_interfaces`, `azure_private_dns_zones`, `azure_public_ip_addresses`, `azure_route_tables`, `azure_virtual_networks`, `azure_local_network_gateways`, `azure_peering_services`, `azure_peerings`, `azure_virtual_network_gateways`, `azure_virtual_wans`, `azure_ddos_protection_plans`, `azure_firewalls`, `azure_ip_groups`, `azure_network_security_groups`, `azure_application_gateways`, `azure_application_security_groups` | Networking. |

See the [Multi-platform guide](multi-platform.md) and [Resource Types §5](resource-types.md#5-beyond-fabric-entra-and-azure).

The following sections document the most commonly used resource types.

### 6.2 lakehouses

Fabric Lakehouse resources with optional OneLake shortcuts and Delta table definitions.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `description` | String | No | | Lakehouse description. |
| `enable_schemas` | Boolean | No | `true` | Enable the lakehouse schemas feature. |
| `sql_endpoint_enabled` | Boolean | No | `true` | Enable the SQL analytics endpoint. |
| `schemas` | List of strings | No | `[]` | Paths to JSON schema files. |
| `shortcuts` | List of [ShortcutConfig](#shortcutconfig) | No | `[]` | OneLake shortcut definitions. |
| `tables` | Map of string to [TableSchema](#tableschema) | No | `{}` | Delta table definitions with schema and partitioning. |

#### ShortcutConfig

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | String | **Yes** | | Shortcut display name. |
| `target` | String | **Yes** | | Target path. Supports `adls://`, `s3://`, `onelake://` protocols. |
| `path` | String | No | `"Tables"` | Shortcut location in lakehouse: `Tables` or `Files`. |
| `connection_id` | String | No | | Connection ID for authenticated shortcuts. |
| `transformation` | Object | No | | Auto-transform files to Delta tables. |

#### TableSchema

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `schema_path` | String | No | | Path to a JSON schema file. |
| `partition_by` | List of strings | No | `[]` | Partition columns. |
| `description` | String | No | | Table description. |

#### Example

```yaml
resources:
  lakehouses:
    bronze_lakehouse:
      description: "Raw data ingestion lakehouse"
      enable_schemas: true
      shortcuts:
        - name: external_sales
          target: "adls://storageaccount.dfs.core.windows.net/container/sales"
          path: Tables
          connection_id: "${var.adls_connection_id}"
          transformation:
            type: file
            source_format: parquet
            destination_table: raw_sales
            sync: true
      tables:
        orders:
          partition_by: [order_date]
          description: "Customer orders"

    gold_lakehouse:
      description: "Curated analytics lakehouse"
```

> **Note**
>
> Lakehouse names support only letters, numbers, and underscores. Hyphens and spaces are not allowed by the Fabric API.

### 6.3 notebooks

Spark notebooks deployed from local `.py` or `.ipynb` files.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | String | **Yes** | | Local path to the notebook file (`.py` or `.ipynb`), relative to the `udp.yml` location. |
| `description` | String | No | | Notebook description. |
| `environment` | String | No | | Reference to an `environments` resource key. |
| `default_lakehouse` | String | No | | Reference to a `lakehouses` resource key. Attached as the default lakehouse. |
| `external_lakehouse` | String | No | | Cross-workspace lakehouse reference using `workspace://ws-name/item` syntax. |
| `spark_properties` | Map of string to string | No | `{}` | Spark configuration overrides. |
| `parameters` | Map of string to any | No | `{}` | Default parameters for notebook execution (used by `udpi30 run`). |
| `folder` | String | No | | Workspace folder path (for example, `ETL/Bronze`). |

#### Example

```yaml
resources:
  notebooks:
    ingest_notebook:
      path: notebooks/ingest.py
      description: "Ingest raw data from external sources"
      environment: spark_env
      default_lakehouse: bronze_lakehouse
      parameters:
        source_path: "/mnt/data/raw"
        batch_size: "1000"
      folder: "ETL/Bronze"

    transform_notebook:
      path: notebooks/transform.ipynb
      description: "Transform bronze to gold"
      environment: spark_env
      default_lakehouse: gold_lakehouse
      spark_properties:
        spark.sql.shuffle.partitions: "200"
      folder: "ETL/Gold"
```

> **Important**
>
> The `environment` and `default_lakehouse` fields must reference resource keys defined in the same deployment. Cross-references are validated at load time. Invalid references cause a validation error.

### 6.4 pipelines

Data Pipelines with optional inline activities and schedules.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | String | No | | Local path to a pipeline JSON definition file. If provided, the JSON is uploaded as the pipeline definition. |
| `description` | String | No | | Pipeline description. |
| `schedule` | [PipelineSchedule](#pipelineschedule) | No | | Schedule configuration. |
| `activities` | List of [PipelineActivity](#pipelineactivity) | No | `[]` | Inline activity definitions. Used when `path` is not provided. |
| `folder` | String | No | | Workspace folder path. |

#### PipelineSchedule

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `frequency` | Enum | No | `"daily"` | Schedule frequency: `once`, `hourly`, `daily`, `weekly`, `monthly`, `cron`. |
| `cron` | String | No | | Cron expression. Required when `frequency` is `cron`. |
| `timezone` | String | No | `"UTC"` | Timezone for the schedule (for example, `America/New_York`). |
| `start_time` | String | No | | ISO 8601 start time. |
| `enabled` | Boolean | No | `true` | Whether the schedule is active. |

#### PipelineActivity

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | String | No | | Activity display name. |
| `notebook` | String | No | | Reference to a `notebooks` resource key. |
| `pipeline` | String | No | | Reference to another `pipelines` resource key (for chained pipelines). |
| `depends_on` | List of strings | No | `[]` | Activity dependencies (names of other activities in this pipeline). |
| `parameters` | Map of string to any | No | `{}` | Parameters to pass to the notebook or pipeline. |

#### Example

```yaml
resources:
  pipelines:
    daily_pipeline:
      description: "Daily ETL orchestration pipeline"
      schedule:
        frequency: cron
        cron: "0 6 * * *"
        timezone: "America/New_York"
        enabled: true
      activities:
        - name: ingest
          notebook: ingest_notebook
          parameters:
            mode: "incremental"
        - name: transform
          notebook: transform_notebook
          depends_on: [ingest]
        - name: refresh_model
          pipeline: refresh_pipeline
          depends_on: [transform]

    manual_pipeline:
      path: pipelines/manual_pipeline.json
      description: "Ad-hoc data loading pipeline"
```

### 6.5 environments

Spark runtime environments with Python library dependencies and Spark configuration.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `runtime` | String | No | `"1.3"` | Spark runtime version (`1.2` or `1.3`). |
| `libraries` | List of strings | No | `[]` | PyPI package specifications (for example, `pandas>=2.0`, `great-expectations`). |
| `conda_dependencies` | List of strings | No | `[]` | Conda package specifications. |
| `spark_properties` | Map of string to string | No | `{}` | Spark configuration properties. |
| `description` | String | No | | Environment description. |

#### Example

```yaml
resources:
  environments:
    spark_env:
      description: "Standard Spark environment for ETL"
      runtime: "1.3"
      libraries:
        - great-expectations>=0.18.0
        - delta-spark>=3.0
        - azure-storage-blob
      spark_properties:
        spark.sql.shuffle.partitions: "200"
        spark.databricks.delta.autoCompact.enabled: "true"
```

### 6.6 warehouses

Fabric Warehouse resources with optional SQL scripts executed on deployment.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `description` | String | No | | Warehouse description. |
| `sql_scripts` | List of strings | No | `[]` | Paths to SQL scripts to execute on deploy. Scripts run in order. |
| `folder` | String | No | | Workspace folder path. |

#### Example

```yaml
resources:
  warehouses:
    analytics_warehouse:
      description: "Central analytics warehouse"
      sql_scripts:
        - sql/create_schemas.sql
        - sql/create_views.sql
        - sql/seed_reference_data.sql
```

> **Note**
>
> Warehouse names support only letters, numbers, and underscores.

### 6.7 semantic_models

Semantic models (Power BI datasets) deployed from local TMDL or BIM definition directories.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | String | **Yes** | | Path to the semantic model definition directory (TMDL format). |
| `description` | String | No | | Model description. |
| `default_lakehouse` | String | No | | Lakehouse resource key for data source binding. |
| `auto_refresh` | Boolean | No | `false` | Automatically refresh the model after deployment. |
| `refresh_timeout` | Integer | No | `600` | Refresh timeout in seconds. |
| `after_deploy` | List of strings | No | `[]` | Actions to run after deployment (for example, `"refresh"`). |
| `depends_on_run` | List of strings | No | `[]` | Only refresh if these resources ran successfully. |
| `folder` | String | No | | Workspace folder path. |

#### Example

```yaml
resources:
  semantic_models:
    sales_model:
      path: semantic_models/sales_model/
      description: "Sales analytics semantic model"
      default_lakehouse: gold_lakehouse
      auto_refresh: true
      refresh_timeout: 900
      folder: "BI/Models"
```

### 6.8 reports

Power BI reports deployed from local `.pbir` or report definition files.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | String | **Yes** | | Path to the `.pbir` or report definition file/directory. |
| `description` | String | No | | Report description. |
| `semantic_model` | String | No | | Reference to a `semantic_models` resource key in the same deployment. |
| `external_semantic_model` | String | No | | Cross-workspace model reference using `workspace://ws-name/model-name` syntax. |
| `folder` | String | No | | Workspace folder path. |

#### Example

```yaml
resources:
  reports:
    sales_report:
      path: reports/sales_report/
      description: "Executive sales dashboard"
      semantic_model: sales_model
      folder: "BI/Reports"

    cross_workspace_report:
      path: reports/cross_ws_report/
      external_semantic_model: "workspace://shared-models/enterprise_model"
```

### 6.9 data_agents

Data Agent (AI/NL2SQL) resources with grounding configuration.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `description` | String | No | | Agent description. |
| `sources` | List of strings | No | `[]` | Resource keys for lakehouses, warehouses, or semantic models to ground the agent on. |
| `instructions` | String | No | | Path to an instructions markdown file. |
| `few_shot_examples` | String | No | | Path to a few-shot examples YAML file. |
| `tables_in_scope` | List of strings | No | `[]` | Specific tables the agent can query. |

#### Example

```yaml
resources:
  data_agents:
    sales_agent:
      description: "Natural language query agent for sales data"
      sources:
        - gold_lakehouse
        - analytics_warehouse
      instructions: agents/sales_instructions.md
      few_shot_examples: agents/sales_examples.yml
      tables_in_scope:
        - orders
        - customers
        - products
```

### 6.10 eventhouses

Eventhouse (KQL database cluster) resources.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `description` | String | No | | Eventhouse description. |
| `kql_scripts` | List of strings | No | `[]` | Paths to KQL scripts to execute on deploy. |
| `retention_days` | Integer | No | | Data retention period in days. |
| `cache_days` | Integer | No | | Hot cache period in days. |

#### Example

```yaml
resources:
  eventhouses:
    telemetry_eventhouse:
      description: "Real-time telemetry event store"
      kql_scripts:
        - kql/create_tables.kql
        - kql/create_functions.kql
      retention_days: 365
      cache_days: 30
```

### 6.11 eventstreams

Eventstream resources for real-time data ingestion.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `description` | String | No | | Eventstream description. |
| `path` | String | No | | Path to an eventstream definition JSON file. |
| `sources` | List of objects | No | `[]` | Eventstream source configurations. |
| `destinations` | List of objects | No | `[]` | Eventstream destination configurations. |

#### Example

```yaml
resources:
  eventstreams:
    iot_stream:
      description: "IoT device telemetry stream"
      path: eventstreams/iot_stream.json
```

---

## 7. security

Workspace and OneLake role assignments. Workspace security roles are a stable feature; OneLake data access roles are in beta. See [Security and Permissions](security.md) for the full guide.

### 7.1 Fields

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `roles` | List of [SecurityRole](#72-securityrole) | No | `[]` | Role definitions. |

### 7.2 SecurityRole

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | String | **Yes** | | Role display name. |
| `entra_group` | String | No | | Entra ID (Azure AD) group name or object ID. |
| `entra_user` | String | No | | Entra ID user UPN (for example, `user@contoso.com`). |
| `service_principal` | String | No | | Service principal name or application ID. |
| `workspace_role` | Enum | No | `"viewer"` | Workspace role: `admin`, `member`, `contributor`, `viewer`. |
| `onelake_roles` | List of [OneLakeRoleBinding](#73-onelakerolebinding) | No | `[]` | Fine-grained OneLake data access roles. |

> **Note**
>
> Specify exactly one of `entra_group`, `entra_user`, or `service_principal` per role.

### 7.3 OneLakeRoleBinding

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `tables` | List of strings | No | `[]` | Table names to grant access to. |
| `folders` | List of strings | No | `[]` | Folder paths to grant access to. |
| `permissions` | List of enum | No | `[]` | Permissions: `read`, `write`, `readwrite`. |

### 7.4 Example

```yaml
security:
  roles:
    - name: data-engineers
      entra_group: "sg-data-engineers"
      workspace_role: contributor
      onelake_roles:
        - tables: [orders, customers]
          folders: ["/Files/raw"]
          permissions: [readwrite]

    - name: analysts
      entra_group: "sg-data-analysts"
      workspace_role: viewer
      onelake_roles:
        - tables: [orders, customers, products]
          permissions: [read]

    - name: ci-cd-deployer
      service_principal: "sp-udp-deploy"
      workspace_role: admin

    - name: report-viewer
      entra_user: "manager@contoso.com"
      workspace_role: viewer
```

---

## 8. connections

Data source connection definitions.

### 8.1 Fields

Each connection is a named entry in the `connections` map.

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `type` | Enum | **Yes** | | Connection type: `adls_gen2`, `sql_server`, `azure_sql`, `cosmos_db`, `kusto`, `http`, `custom`. |
| `endpoint` | String | No | | Connection endpoint URL or server address. |
| `database` | String | No | | Database name (for database-type connections). |
| `auth_method` | String | No | | Authentication method (for example, `service_principal`, `managed_identity`, `key`). |
| `connection_string_var` | String | No | | Environment variable name containing the connection string. |
| `properties` | Map of string to string | No | `{}` | Additional connection properties. |

### 8.2 Example

```yaml
connections:
  source_adls:
    type: adls_gen2
    endpoint: "https://mystorage.dfs.core.windows.net"
    auth_method: managed_identity

  source_sql:
    type: azure_sql
    endpoint: "myserver.database.windows.net"
    database: "salesdb"
    auth_method: service_principal

  external_api:
    type: http
    endpoint: "https://api.example.com/v2"
    properties:
      api_version: "2024-01-01"
      timeout: "30"

  source_cosmos:
    type: cosmos_db
    endpoint: "https://myaccount.documents.azure.com:443/"
    database: "telemetry"
    auth_method: key
    connection_string_var: "COSMOS_CONNECTION_STRING"
```

> **Warning**
>
> Never put secrets (connection strings, API keys, passwords) directly in `udp.yml`. Use `${keyvault.VAULT.SECRET}` for Key Vault references, `${secret.NAME}` or `${env.VAR_NAME}` for environment variables, or the `connection_string_var` field.

### 8.3 Connectivity check

`udpi30 validate` and `udpi30 diag` verify that each connection's source is reachable. For every connection they derive a `host:port` — from the resolved connection string (`connection_string_var` + secrets) when present, otherwise from `endpoint` — and open a TCP socket to confirm the source accepts connections. The default port follows the `type` (`sql_server`/`azure_sql` → 1433, everything else → 443) unless the connection string or endpoint specifies one.

This is a **network reachability** check, not an authenticated handshake. In `validate`, an unreachable source is a warning by default (use `--strict` to fail, or `--skip-connection-check` to disable); in `diag` it is a pass/fail check. Connections with no resolvable connection string or endpoint are skipped.

---

## 9. policies

Validation and governance rules enforced during `udpi30 validate` and `udpi30 deploy`.

### 9.1 Fields

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `rules` | List of [PolicyRule](#92-policyrule) | No | `[]` | Custom policy rules. |
| `require_description` | Boolean | No | `false` | Require a `description` field on every resource. |
| `naming_convention` | String | No | | Naming convention to enforce: `snake_case`, `camelCase`, etc. |
| `max_notebook_size_kb` | Integer | No | | Maximum allowed notebook file size in kilobytes. |
| `blocked_libraries` | List of strings | No | `[]` | PyPI packages that are not allowed in environment definitions. |

### 9.2 PolicyRule

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | String | **Yes** | | Rule display name. |
| `check` | String | **Yes** | | Policy check type (for example, `require_description`, `naming_convention`, `max_resources`). |
| `value` | Any | No | | Check-specific value. |
| `severity` | String | No | `"error"` | Severity level: `error` (blocks deploy) or `warning` (informational). |

### 9.3 Example

```yaml
policies:
  require_description: true
  naming_convention: snake_case
  max_notebook_size_kb: 500
  blocked_libraries:
    - tensorflow   # Use ml_models instead
    - boto3        # Use Fabric-native connections
  rules:
    - name: max-resources
      check: max_resources
      value: 50
      severity: warning
    - name: require-env
      check: require_environment
      severity: error
```

> **Note**
>
> Only the four built-in options (`require_description`, `naming_convention`, `max_notebook_size_kb`, `blocked_libraries`) are enforced at validation time. Entries in `rules` are accepted by the schema but not yet evaluated; see [Policy Enforcement](../advanced/policies.md). The `blocked_libraries` match is by library name prefix, so list bare names rather than version specifiers.

---

## 10. notifications

Webhook notifications sent after deployment events.

### 10.1 Fields

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `on_success` | List of [NotificationConfig](#102-notificationconfig) | No | `[]` | Notifications to send after a successful deployment. |
| `on_failure` | List of [NotificationConfig](#102-notificationconfig) | No | `[]` | Notifications to send after a failed deployment. |

### 10.2 NotificationConfig

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `type` | String | **Yes** | | Notification type: `slack`, `teams`. |
| `webhook` | String | **Yes** | | Webhook URL. Use `${secret.WEBHOOK}` for secrets. |
| `message` | String | No | `"Deployed {deployment.name} v{deployment.version} to {target}"` | Message template. Supports `{deployment.name}`, `{deployment.version}`, `{target}` placeholders. |

### 10.3 Example

```yaml
notifications:
  on_success:
    - type: slack
      webhook: "${secret.SLACK_DEPLOY_WEBHOOK}"
      message: "Deployed {deployment.name} v{deployment.version} to {target}"
    - type: teams
      webhook: "${secret.TEAMS_WEBHOOK}"

  on_failure:
    - type: slack
      webhook: "${secret.SLACK_ALERTS_WEBHOOK}"
      message: "FAILED: {deployment.name} v{deployment.version} deployment to {target}"
```

---

## 11. state

State backend configuration. By default, deployment state is stored locally as JSON in a `.udpi30/` directory.

### 11.1 Fields

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `backend` | String | No | `"local"` | State backend type: `local`, `azureblob`, `onelake`, `adls`. |
| `config` | Map of string to string | No | `{}` | Backend-specific configuration. |

Backend availability:

| Backend | Storage | Status |
|---|---|---|
| `local` | JSON files in `.udpi30/` | Stable |
| `azureblob` | Azure Blob Storage | Beta |
| `onelake` | Fabric lakehouse Files area | Beta |
| `adls` | Azure Data Lake Storage Gen2 | Beta |

### 11.2 Backend: local (default)

State is stored per target in `.udpi30/state-<target>.json` in the project directory.

```yaml
state:
  backend: local
```

### 11.3 Backend: azureblob

State is stored in Azure Blob Storage. This backend is in beta.

```yaml
state:
  backend: azureblob
  config:
    account_name: "mystorageaccount"
    container_name: "udpi30-state"   # Optional, default: udpi30-state
    prefix: "sales-analytics"          # Optional key prefix
    # account_key: optional; omit so DefaultAzureCredential is used
```

### 11.4 Backend: onelake

State is stored in the Files area of a Fabric lakehouse, alongside your data. This backend is in beta.

```yaml
state:
  backend: onelake
  config:
    workspace_id: "your-workspace-guid"
    lakehouse_id: "your-lakehouse-guid"
    path: ".udpi30-state"   # Optional, default: .udpi30-state
```

### 11.5 Backend: adls

State is stored in Azure Data Lake Storage Gen2. This backend is in beta.

```yaml
state:
  backend: adls
  config:
    account_name: "mydatalake"
    filesystem: "udpi30-state"   # Optional, default: udpi30-state
    prefix: "sales-analytics"      # Optional key prefix
```

> **Tip**
>
> Use a remote state backend when multiple team members or CI/CD pipelines deploy the same deployment to prevent state conflicts.

---

## 12. targets

Environment-specific overrides. Each target defines a deployment context with its own workspace, variables, security, run identity, post-deploy checks, and deployment strategy.

### 12.1 Fields

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `default` | Boolean | No | `false` | Whether this is the default target when `-t` is not specified. Only one target should be set as default. |
| `workspace` | [WorkspaceConfig](#4-workspace) | No | | Workspace overrides for this target. Merges with the top-level `workspace`. |
| `variables` | Map of string to string | No | `{}` | Variable values for this target. Overrides the top-level `variables` defaults. |
| `run_as` | [RunAsConfig](#122-runasconfig) | No | | Identity to use for deployment. |
| `security` | [SecurityConfig](#7-security) | No | | Target-specific security role overrides. |
| `resources` | [ResourceOverrides](#125-resourceoverrides) | No | | Per-resource property overrides for this target. |
| `post_deploy` | List of [ValidationCheck](#123-validationcheck) | No | `[]` | Post-deployment validation checks. |
| `deployment_strategy` | [DeploymentStrategy](#124-deploymentstrategy) | No | | Deployment strategy (all-at-once or canary). |

### 12.2 RunAsConfig

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `service_principal` | String | No | | Service principal name or app ID to deploy as. |
| `user_name` | String | No | | User UPN to deploy as. |

### 12.3 ValidationCheck

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `run` | String | No | | Resource name to execute as a validation. |
| `sql` | String | No | | SQL query to execute as a validation. |
| `expect` | String | No | | Expected result (for example, `success`, `> 0`). |
| `timeout` | Integer | No | `300` | Timeout in seconds. |

### 12.4 DeploymentStrategy

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `type` | String | No | `"all-at-once"` | Strategy type: `all-at-once` or `canary`. |
| `canary_resources` | List of strings | No | `[]` | Resource keys to deploy first in a canary deployment. |
| `validation` | [ValidationCheck](#123-validationcheck) | No | | Validation to run after canary resources are deployed, before proceeding with the rest. |

### 12.5 ResourceOverrides

Per-target property overrides for specific resources. Each resource type key maps resource names to a dictionary of field overrides.

| Field | Type | Description |
|---|---|---|
| `lakehouses` | Map | Per-lakehouse overrides. |
| `notebooks` | Map | Per-notebook overrides. |
| `pipelines` | Map | Per-pipeline overrides. |
| `warehouses` | Map | Per-warehouse overrides. |
| `semantic_models` | Map | Per-semantic-model overrides. |
| `reports` | Map | Per-report overrides. |
| `data_agents` | Map | Per-data-agent overrides. |
| `environments` | Map | Per-environment overrides. |
| `eventhouses` | Map | Per-eventhouse overrides. |
| `eventstreams` | Map | Per-eventstream overrides. |

### 12.6 Example

```yaml
targets:
  dev:
    default: true
    workspace:
      name: sales-analytics-dev
      capacity_id: "aaaabbbb-cccc-dddd-eeee-ffffffffffff"
    variables:
      source_connection: "Server=dev-sql;Database=sales"
      environment: "development"
    post_deploy:
      - run: smoke_test_notebook
        expect: success
        timeout: 300

  staging:
    workspace:
      name: sales-analytics-staging
      capacity_id: "11112222-3333-4444-5555-666677778888"
    variables:
      source_connection: "Server=staging-sql;Database=sales"
      environment: "staging"
    run_as:
      service_principal: sp-udp-staging
    post_deploy:
      - run: integration_test_notebook
        expect: success
        timeout: 600
      - sql: "SELECT COUNT(*) FROM gold_lakehouse.orders WHERE load_date = CURRENT_DATE()"
        expect: "> 0"
        timeout: 60

  prod:
    workspace:
      name: sales-analytics-prod
      capacity_id: "99998888-7777-6666-5555-444433332222"
    variables:
      source_connection: "${secret.PROD_SQL_CONNECTION}"
      environment: "production"
    run_as:
      service_principal: sp-udp-prod
    security:
      roles:
        - name: prod-admins
          entra_group: "sg-prod-admins"
          workspace_role: admin
    deployment_strategy:
      type: canary
      canary_resources:
        - ingest_notebook
      validation:
        run: smoke_test_notebook
        expect: success
        timeout: 300
    resources:
      notebooks:
        ingest_notebook:
          spark_properties:
            spark.sql.shuffle.partitions: "400"
```

> **Tip**
>
> Use the `deployment_strategy` with `type: canary` for production targets. This deploys a subset of resources first, runs validation, and only proceeds with the full deployment if validation passes.

---

## 13. admin

Tenant-level (admin) settings, applied **tenant-wide** via the Fabric Admin API. Unlike `resources`, these are not workspace items and are never applied by `deploy` — they are applied only by the gated [`udpi30 admin apply`](../cli/commands.md#admin-apply) command. See [Admin / Tenant Settings](admin-settings.md) for the full guide.

### 13.1 Fields

| Field | Type | Description |
|---|---|---|
| `tenant_settings` | map | Tenant settings keyed by their API `settingName` (e.g. `PublishToWeb`), not the portal display title. |

### 13.2 TenantSetting

| Field | Type | Description |
|---|---|---|
| `enabled` | boolean (required) | Enable (`true`) or disable (`false`) the setting. |
| `delegate_to_capacity` | boolean | Allow a capacity admin to override. Only applied when declared. |
| `delegate_to_domain` | boolean | Allow a domain admin to override. Only applied when declared. |
| `delegate_to_workspace` | boolean | Allow a workspace admin to override. Only applied when declared. |
| `enabled_security_groups` | list | Security groups the setting is enabled for (`graph_id` + `name`). |
| `excluded_security_groups` | list | Security groups explicitly excluded. |
| `properties` | list | Typed properties some settings require (`name`, `value`, `type`). |

`type` is one of `FreeText`, `Url`, `Boolean`, `MailEnabledSecurityGroup`, `Integer`. Only settings, groups, and properties you explicitly declare are managed — nothing is cleared implicitly.

### 13.3 Example

```yaml
admin:
  tenant_settings:
    # Disable Publish to web org-wide
    PublishToWeb:
      enabled: false

    # Enable a feature only for a security group, with workspace delegation
    DevelopmentTenantSettings:
      enabled: true
      delegate_to_workspace: true
      enabled_security_groups:
        - graph_id: "f51b705f-a409-4d40-9197-c5d5f349e2f0"
          name: "Data Engineers"
```

> **Note**
>
> Setting names are the API `settingName` identifiers, which the [tenant settings index](https://learn.microsoft.com/en-us/fabric/admin/tenant-settings-index) does not list (it shows display titles). Run `udpi30 admin plan` to validate your names against the live tenant; unknown names are reported.

---

## 14. include

Merge additional YAML files into the deployment definition. Use `include` to split large deployments across multiple files.

### 14.1 Fields

| Type | Description |
|---|---|
| List of strings | File paths or glob patterns relative to the `udp.yml` location. |

### 14.2 Example

**Main udp.yml:**

```yaml
deployment:
  name: sales-analytics
  version: "1.0.0"

include:
  - resources/lakehouses.yml
  - resources/notebooks.yml
  - resources/pipelines.yml
  - security/*.yml
```

**resources/lakehouses.yml:**

```yaml
resources:
  lakehouses:
    bronze_lakehouse:
      description: "Raw ingestion lakehouse"
    gold_lakehouse:
      description: "Curated analytics lakehouse"
```

**resources/notebooks.yml:**

```yaml
resources:
  notebooks:
    ingest_notebook:
      path: notebooks/ingest.py
      default_lakehouse: bronze_lakehouse
```

> **Note**
>
> Included files are deep-merged into the main deployment. If the same resource key appears in multiple files, the last included file wins.

---

## 15. extends

Inherit from a parent deployment definition. The child deployment inherits all settings from the parent and can override any field.

### 15.1 Fields

| Type | Description |
|---|---|
| String | Path to the parent `udp.yml` file, relative to the child deployment location. |

### 15.2 Example

**Parent deployment (shared/udp.yml):**

```yaml
deployment:
  name: shared-platform
  version: "1.0.0"

workspace:
  capacity_id: "aaaabbbb-cccc-dddd-eeee-ffffffffffff"

resources:
  environments:
    standard_env:
      runtime: "1.3"
      libraries:
        - great-expectations>=0.18.0
        - delta-spark>=3.0
```

**Child deployment (sales/udp.yml):**

```yaml
extends: ../shared/udp.yml

deployment:
  name: sales-analytics
  version: "1.0.0"

resources:
  notebooks:
    ingest_notebook:
      path: notebooks/ingest.py
      environment: standard_env   # Inherited from parent
```

> **Note**
>
> The child deployment's `deployment.name` overrides the parent's name. All other fields are deep-merged, with child values taking precedence.

---

## 16. Complete example

The following `udp.yml` demonstrates most features:

```yaml
deployment:
  name: sales-analytics
  version: "2.0.0"
  description: "End-to-end sales analytics with medallion architecture"

workspace:
  capacity_id: "${var.capacity_id}"
  description: "Sales analytics workspace"

variables:
  capacity_id:
    description: "Fabric capacity GUID"
  source_connection:
    description: "Source database connection string"
    default: "Server=localhost;Database=sales"
  environment:
    description: "Deployment environment"
    default: "dev"

resources:
  environments:
    spark_env:
      runtime: "1.3"
      libraries:
        - great-expectations>=0.18.0
        - delta-spark>=3.0

  lakehouses:
    bronze_lakehouse:
      description: "Raw data ingestion"
      shortcuts:
        - name: external_data
          target: "adls://storage.dfs.core.windows.net/raw"
    gold_lakehouse:
      description: "Curated analytics data"

  notebooks:
    ingest_notebook:
      path: notebooks/ingest.py
      environment: spark_env
      default_lakehouse: bronze_lakehouse
    transform_notebook:
      path: notebooks/transform.py
      environment: spark_env
      default_lakehouse: gold_lakehouse

  pipelines:
    daily_pipeline:
      schedule:
        frequency: daily
        timezone: "America/New_York"
      activities:
        - name: ingest
          notebook: ingest_notebook
        - name: transform
          notebook: transform_notebook
          depends_on: [ingest]

  semantic_models:
    sales_model:
      path: models/sales/
      default_lakehouse: gold_lakehouse
      auto_refresh: true

  reports:
    sales_dashboard:
      path: reports/sales_dashboard/
      semantic_model: sales_model

  data_agents:
    sales_agent:
      sources: [gold_lakehouse]
      instructions: agents/instructions.md

security:
  roles:
    - name: engineers
      entra_group: "sg-data-engineers"
      workspace_role: contributor
    - name: analysts
      entra_group: "sg-analysts"
      workspace_role: viewer

connections:
  source_sql:
    type: azure_sql
    endpoint: "myserver.database.windows.net"
    database: "salesdb"

policies:
  require_description: true
  naming_convention: snake_case
  max_notebook_size_kb: 500

notifications:
  on_success:
    - type: slack
      webhook: "${secret.SLACK_WEBHOOK}"
  on_failure:
    - type: slack
      webhook: "${secret.SLACK_ALERTS_WEBHOOK}"
      message: "FAILED: {deployment.name} to {target}"

state:
  backend: azureblob
  config:
    storage_account: "statestore"
    container: "udpi30"
    key: "sales-analytics"

targets:
  dev:
    default: true
    workspace:
      name: sales-analytics-dev
    variables:
      capacity_id: "dev-capacity-guid-here"
      source_connection: "Server=dev-sql;Database=sales"
    post_deploy:
      - run: ingest_notebook
        expect: success
        timeout: 300

  prod:
    workspace:
      name: sales-analytics-prod
    variables:
      capacity_id: "prod-capacity-guid-here"
      source_connection: "${secret.PROD_CONNECTION_STRING}"
    run_as:
      service_principal: sp-udp-prod
    deployment_strategy:
      type: canary
      canary_resources: [ingest_notebook]
      validation:
        run: ingest_notebook
        expect: success
```

---

## 17. See also

- [CLI command reference](../cli/commands.md) -- Full reference for all `udpi30` commands.
- [Admin / Tenant Settings](admin-settings.md) -- Declaratively manage org-wide Fabric tenant settings.
- [Installation](../getting-started/installation.md) -- Install and configure Unified Data Platform Deployment.
