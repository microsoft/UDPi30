# Resource Types

This page is the reference for every Fabric item type that udpi30 can manage: the type key used in `udp.yml`, the underlying Fabric API type, the accepted definition formats, and a full YAML example for each resource. udpi30 covers 46 Fabric item types, of which 30 are verified against live Fabric workspaces.

!!! info "Beyond Fabric — Entra & Azure"
    A single `udp.yml` can also declare **Microsoft Entra** directory objects and
    **Azure** (ARM) resources alongside Fabric items. Those types are documented
    in [§5](#5-beyond-fabric-entra-and-azure) and in the dedicated
    [Multi-platform guide](multi-platform.md).

---

## 1. Coverage and deployment order

udpi30 supports 46 Fabric item types. 30 of these are verified live; the remainder are defined against the published Fabric REST API and pending live verification. A small number of types are list-only because the Fabric API does not support creating them.

Resources deploy in dependency order, resolved automatically by the engine:

| Order | Rule |
|---|---|
| 1 | Environments deploy before lakehouses |
| 2 | Lakehouses deploy before notebooks, warehouses, and semantic models |
| 3 | Notebooks deploy before pipelines |
| 4 | Semantic models deploy before reports and data agents |

You do not declare this ordering in `udp.yml`; the Resolver derives it from resource references (for example, a notebook's `default_lakehouse` or a report's `semantic_model`).

!!! tip "Workspace folders by type"
    Every resource type belongs to a workspace folder category (Notebooks,
    Pipelines, Lakehouses, Models, Databases, Real-Time, …). Set
    [`workspace.folders_by_type: true`](udp-yml.md#44-workspace-folders-by-type)
    to have `deploy` group items into those folders automatically.

---

## 2. Supported types

### 2.1 Data Engineering

| Resource | Type Key | API Type | Definitions |
|----------|----------|----------|-------------|
| Lakehouse | `lakehouses` | Lakehouse | Shortcuts, tables, schemas |
| Notebook | `notebooks` | Notebook | .py, .ipynb, .sql, .scala, .r |
| Environment | `environments` | Environment | Runtime, libraries, Spark config |
| Spark Job Definition | `spark_job_definitions` | SparkJobDefinition | .py, .jar |
| GraphQL API | `graphql_apis` | GraphQLApi | Schema file |
| Snowflake Database | `snowflake_databases` | SnowflakeDatabase | Connection-based |
| Materialized Lake View | `materialized_lake_views` | MaterializedLakeView | SQL definition |

### 2.2 Data Factory

| Resource | Type Key | API Type | Definitions |
|----------|----------|----------|-------------|
| Data Pipeline | `pipelines` | DataPipeline | YAML activities or JSON |
| Copy Job | `copy_jobs` | CopyJob | JSON definition |
| Mounted Data Factory | `mounted_data_factories` | MountedDataFactory | Metadata |
| Apache Airflow Job | `airflow_jobs` | ApacheAirflowJob | DAG file |
| dbt Job | `dbt_jobs` | DataBuildToolJob | dbt project |

### 2.3 Data Warehouse

| Resource | Type Key | API Type | Definitions |
|----------|----------|----------|-------------|
| Warehouse | `warehouses` | Warehouse | SQL scripts |
| SQL Database | `sql_databases` | SQLDatabase | SQL scripts |
| Mirrored Database | `mirrored_databases` | MirroredDatabase | Connection-based |
| Mirrored Warehouse | `mirrored_warehouses` | MirroredWarehouse | List-only; cannot be created via API |
| Mirrored Databricks Catalog | `mirrored_databricks_catalogs` | MirroredAzureDatabricksCatalog | Connection-based |
| Cosmos DB Database | `cosmosdb_databases` | CosmosDBDatabase | Connection-based |
| Datamart | `datamarts` | Datamart | List-only; cannot be created via API |

### 2.4 Power BI

| Resource | Type Key | API Type | Definitions |
|----------|----------|----------|-------------|
| Semantic Model | `semantic_models` | SemanticModel | TMDL or TMSL |
| Report | `reports` | Report | PBIR format |
| Paginated Report | `paginated_reports` | PaginatedReport | List-only; cannot be created via API |
| Dashboard | `dashboards` | Dashboard | List-only; cannot be created via API |
| Dataflow | `dataflows` | Dataflow | Not supported by Fabric API |

### 2.5 Data Science

| Resource | Type Key | API Type | Definitions |
|----------|----------|----------|-------------|
| ML Model | `ml_models` | MLModel | MLflow model |
| ML Experiment | `ml_experiments` | MLExperiment | Metadata |

### 2.6 Real-Time Intelligence

| Resource | Type Key | API Type | Definitions |
|----------|----------|----------|-------------|
| Eventhouse | `eventhouses` | Eventhouse | KQL scripts |
| Eventstream | `eventstreams` | Eventstream | JSON definition |
| KQL Database | `kql_databases` | KQLDatabase | KQL scripts |
| KQL Dashboard | `kql_dashboards` | KQLDashboard | Definition file |
| KQL Queryset | `kql_querysets` | KQLQueryset | Definition file |
| Reflex (Data Activator) | `reflex` | Reflex | JSON definition |
| Digital Twin Builder | `digital_twin_builders` | DigitalTwinBuilder | Definition file |
| Digital Twin Builder Flow | `digital_twin_builder_flows` | DigitalTwinBuilderFlow | Definition file |
| Event Schema Set | `event_schema_sets` | EventSchemaSet | Definition file |
| Graph Query Set | `graph_query_sets` | GraphQuerySet | Definition file |

### 2.7 AI & Knowledge

| Resource | Type Key | API Type | Definitions |
|----------|----------|----------|-------------|
| Data Agent | `data_agents` | DataAgent | Instructions + examples |
| Operations Agent | `operations_agents` | OperationsAgent | Instructions |
| Anomaly Detector | `anomaly_detectors` | AnomalyDetector | Configuration |
| Ontology | `ontologies` | Ontology | Definition file |

### 2.8 Other

| Resource | Type Key | API Type | Definitions |
|----------|----------|----------|-------------|
| Variable Library | `variable_libraries` | VariableLibrary | Key-value pairs |
| User Data Function | `user_data_functions` | UserDataFunction | Function definition |
| Graph | `graphs` | Graph | Definition file |
| Graph Model | `graph_models` | GraphModel | Definition file |
| Map | `map_items` | Map | Definition file |
| HLS Cohort | `hls_cohorts` | HLSCohort | Definition file |

---

## 3. OneLake shortcuts

Shortcuts are not a separate item type; they are sub-resources of lakehouses.

```yaml
lakehouses:
  bronze_lakehouse:
    shortcuts:
      - name: external_data
        target: "adls://storageaccount/container/path"
        path: Tables
        connection_id: "optional-connection-guid"
      - name: s3_data
        target: "s3://bucket-name/prefix"
      - name: cross_workspace
        target: "onelake://workspace-id/item-id/Tables/my_table"
```

Supported shortcut targets:

| Scheme | Target |
|---|---|
| `adls://` | Azure Data Lake Storage Gen2 |
| `s3://` | Amazon S3 |
| `onelake://` | Cross-workspace OneLake reference |

### 3.1 Shortcut transformations

Transformations auto-convert source files to managed Delta tables that stay in sync with the source, without pipelines.

**File transformations** convert CSV, Parquet, JSON, or Excel files into Delta tables:

```yaml
lakehouses:
  bronze_lakehouse:
    shortcuts:
      - name: csv_sales_data
        target: "adls://datalake/sales/*.csv"
        path: Files
        transformation:
          type: file
          source_format: csv
          destination_table: raw_sales
          sync: true
          flatten: false

      - name: nested_json_events
        target: "adls://datalake/events/*.json"
        path: Files
        transformation:
          type: file
          source_format: json
          destination_table: raw_events
          flatten: true
          compression: gzip

      - name: excel_reports
        target: "adls://datalake/finance/*.xlsx"
        path: Files
        transformation:
          type: file
          source_format: excel
          destination_table: finance_reports
```

**AI-powered transformations** apply summarization, translation, or classification:

```yaml
lakehouses:
  documents_lakehouse:
    shortcuts:
      - name: support_tickets
        target: "adls://datalake/tickets/*.json"
        path: Files
        transformation:
          type: ai
          ai_skill: summarize
          destination_table: ticket_summaries

      - name: multilingual_docs
        target: "adls://datalake/docs/*.json"
        path: Files
        transformation:
          type: ai
          ai_skill: translate
          ai_prompt: "Translate to English"
          destination_table: docs_english

      - name: email_classification
        target: "adls://datalake/emails/*.json"
        path: Files
        transformation:
          type: ai
          ai_skill: classify
          ai_prompt: "Classify as: complaint, inquiry, feedback, spam"
          destination_table: classified_emails
```

---

## 4. YAML reference: all resource types

### 4.1 Data Engineering

#### Lakehouse

```yaml
lakehouses:
  bronze_lakehouse:
    description: "Raw data landing zone"
    enable_schemas: true
    tables:
      raw_orders:
        schema_path: ./schemas/orders.json
        partition_by: [order_date]
    shortcuts:
      - name: external_data
        target: "adls://account/container/path"
        path: Tables
        connection_id: "optional-guid"
        transformation:
          type: file
          source_format: csv
          destination_table: raw_external
```

#### Notebook

```yaml
notebooks:
  etl_pipeline:
    path: ./notebooks/etl.py
    description: "ETL pipeline"
    environment: spark_env
    default_lakehouse: bronze_lakehouse
    parameters:
      batch_size: 1000
      source_table: orders
    folder: ETL/Bronze
```

#### Environment

```yaml
environments:
  spark_env:
    runtime: "1.3"
    libraries:
      - semantic-link-labs
      - delta-spark
    conda_dependencies:
      - numpy=1.24
    spark_properties:
      spark.sql.shuffle.partitions: "200"
```

#### Spark Job Definition

```yaml
spark_job_definitions:
  distributed_training:
    path: ./spark_jobs/train.py
    description: "Distributed model training"
    environment: spark_env
    default_lakehouse: feature_store
    args: ["--epochs", "10", "--batch-size", "256"]
    conf:
      spark.executor.memory: "8g"
      spark.executor.cores: "4"
```

#### GraphQL API

```yaml
graphql_apis:
  product_api:
    description: "GraphQL API over product data"
    path: ./graphql/schema.graphql
    data_source: gold_lakehouse
```

#### Snowflake Database

```yaml
snowflake_databases:
  snowflake_mirror:
    description: "Mirrored Snowflake data"
    connection: snowflake_conn
```

#### Materialized Lake View

```yaml
materialized_lake_views:
  device_daily_summary:
    description: "Daily per-device telemetry rollup"
    path: ./sql/device_daily_summary.sql
    default_lakehouse: telemetry_lakehouse
    refresh_cron: "0 2 * * *"
```

### 4.2 Data Factory

#### Data Pipeline

```yaml
pipelines:
  daily_refresh:
    description: "Daily ETL pipeline"
    schedule:
      cron: "0 6 * * *"
      timezone: America/Chicago
      enabled: true
    activities:
      - name: ingest
        notebook: ingest_notebook
      - name: transform
        notebook: transform_notebook
        depends_on: [ingest]
      - name: load
        notebook: load_notebook
        depends_on: [transform]
```

#### Copy Job

```yaml
copy_jobs:
  copy_sales_data:
    description: "Copy sales data from Azure SQL"
    path: ./copy_jobs/sales_copy.json
```

#### Mounted Data Factory

```yaml
mounted_data_factories:
  legacy_adf:
    description: "Mounted Azure Data Factory for legacy pipelines"
    data_factory_id: "/subscriptions/.../resourceGroups/.../providers/Microsoft.DataFactory/factories/udp-adf"
```

#### Apache Airflow Job

```yaml
airflow_jobs:
  airflow_etl:
    description: "Airflow DAG for complex orchestration"
    path: ./dags/etl_dag.py
```

#### dbt Job

```yaml
dbt_jobs:
  dbt_transform:
    description: "dbt transformation project"
    path: ./dbt_project/
    environment: spark_env
```

### 4.3 Data Warehouse

#### Warehouse

```yaml
warehouses:
  analytics_warehouse:
    description: "SQL analytics warehouse"
    sql_scripts:
      - ./sql/create_views.sql
      - ./sql/create_procedures.sql
```

#### SQL Database

```yaml
sql_databases:
  operational_db:
    description: "Operational SQL database"
    sql_scripts:
      - ./sql/schema.sql
      - ./sql/seed_data.sql
```

#### Mirrored Database

```yaml
mirrored_databases:
  azure_sql_mirror:
    description: "Mirrored Azure SQL database"
    source_type: "Azure SQL"
    connection: azure_sql_conn
```

#### Mirrored Warehouse

```yaml
mirrored_warehouses:
  synapse_mirror:
    description: "Mirrored Synapse warehouse"
    source_type: "Synapse"
```

#### Mirrored Databricks Catalog

```yaml
mirrored_databricks_catalogs:
  databricks_catalog:
    description: "Mirrored Databricks Unity Catalog"
    connection: databricks_conn
```

#### Cosmos DB Database

```yaml
cosmosdb_databases:
  cosmos_mirror:
    description: "Mirrored Cosmos DB data"
    connection: cosmos_conn
```

#### Datamart

```yaml
datamarts:
  sales_datamart:
    description: "Self-service sales datamart"
    path: ./datamarts/sales_definition.json
```

### 4.4 Power BI

#### Semantic Model

```yaml
semantic_models:
  analytics_model:
    path: ./semantic_model/
    description: "Semantic model over gold lakehouse"
    default_lakehouse: gold_lakehouse
    auto_refresh: true
    refresh_timeout: 600
    folder: Models
```

#### Report

```yaml
reports:
  executive_dashboard:
    path: ./reports/dashboard/
    description: "Executive dashboard (PBIR format)"
    semantic_model: analytics_model
    folder: Reports
```

#### Paginated Report

```yaml
paginated_reports:
  monthly_invoice:
    description: "Monthly invoice report (RDL)"
    path: ./reports/invoice.rdl
    data_source: analytics_warehouse
```

#### Dashboard

```yaml
dashboards:
  overview_dashboard:
    description: "High-level KPI dashboard"
```

#### Dataflow

```yaml
dataflows:
  customer_transform:
    description: "Dataflow Gen2 for customer data"
    path: ./dataflows/customer_transform.json
```

### 4.5 Data Science

#### ML Model

```yaml
ml_models:
  churn_model:
    path: ./models/churn_model/
    description: "Customer churn prediction model"
    framework: xgboost
```

#### ML Experiment

```yaml
ml_experiments:
  churn_experiment:
    description: "Churn prediction experiment tracking"
```

### 4.6 Real-Time Intelligence

#### Eventhouse

```yaml
eventhouses:
  telemetry_eventhouse:
    description: "IoT telemetry eventhouse"
    kql_scripts:
      - ./kql/create_tables.kql
      - ./kql/create_functions.kql
    retention_days: 365
    cache_days: 31
```

#### Eventstream

```yaml
eventstreams:
  device_events:
    description: "Real-time device event stream"
    path: ./eventstreams/device_config.json
    sources:
      - type: event_hub
        name: iot-hub-events
    destinations:
      - type: eventhouse
        name: telemetry_eventhouse
```

#### KQL Database

```yaml
kql_databases:
  telemetry_db:
    description: "KQL database for device telemetry"
    parent_eventhouse: telemetry_eventhouse
    kql_scripts:
      - ./kql/create_tables.kql
```

#### KQL Dashboard

```yaml
kql_dashboards:
  ops_dashboard:
    description: "Real-time operations dashboard"
    path: ./dashboards/ops_dashboard.json
    data_source: telemetry_db
```

#### KQL Queryset

```yaml
kql_querysets:
  telemetry_queries:
    description: "Pre-built KQL queries for analysis"
    path: ./kql/querysets/
    data_source: telemetry_db
```

#### Reflex (Data Activator)

```yaml
reflex:
  anomaly_alerts:
    description: "Trigger alerts on anomalous readings"
    path: ./reflex/anomaly_rules.json
```

#### Digital Twin Builder

```yaml
digital_twin_builders:
  factory_twin:
    description: "Digital twin of factory floor"
    path: ./twins/factory_definition.json
```

#### Digital Twin Builder Flow

```yaml
digital_twin_builder_flows:
  factory_flow:
    description: "Data flow for factory twin"
    path: ./twins/factory_flow.json
    twin_builder: factory_twin
```

#### Event Schema Set

```yaml
event_schema_sets:
  device_schemas:
    description: "Schema definitions for IoT events"
    path: ./schemas/device_events.json
```

#### Graph Query Set

```yaml
graph_query_sets:
  network_queries:
    description: "Graph queries for network analysis"
    path: ./graph/queries/
    data_source: telemetry_db
```

### 4.7 AI & Knowledge

#### Data Agent

```yaml
data_agents:
  analytics_agent:
    description: "Natural language interface to your data"
    sources:
      - gold_lakehouse
      - analytics_warehouse
    instructions: ./agent/instructions.md
    few_shot_examples: ./agent/examples.yaml
    tables_in_scope:
      - daily_order_summary
      - customer_360
```

#### Operations Agent

```yaml
operations_agents:
  ops_agent:
    description: "Operations monitoring agent"
    sources:
      - telemetry_eventhouse
    instructions: ./agent/ops_instructions.md
```

#### Anomaly Detector

```yaml
anomaly_detectors:
  revenue_detector:
    description: "Detect revenue anomalies"
    data_source: gold_lakehouse
    path: ./detectors/revenue_config.json
```

#### Ontology

```yaml
ontologies:
  business_ontology:
    description: "Business domain knowledge graph"
    path: ./ontology/definition.json
    data_sources:
      - gold_lakehouse
      - analytics_warehouse
```

### 4.8 Other

#### Variable Library

```yaml
variable_libraries:
  shared_config:
    description: "Shared configuration variables"
    variables:
      environment: production
      region: us-east
      log_level: info
      max_retries: "3"
```

#### User Data Function

```yaml
user_data_functions:
  custom_transform:
    description: "Custom data transformation function"
    path: ./functions/transform.py
    runtime: python
```

#### Graph

```yaml
graphs:
  knowledge_graph:
    description: "Product knowledge graph"
    path: ./graph/definition.json
    data_source: gold_lakehouse
```

#### Graph Model

```yaml
graph_models:
  supply_chain_model:
    description: "Supply chain graph model"
    path: ./graph/supply_chain.json
    data_source: analytics_warehouse
```

#### Map

```yaml
map_items:
  geo_mapping:
    description: "Geographic data mapping"
    path: ./maps/geo_config.json
```

#### HLS Cohort

```yaml
hls_cohorts:
  patient_cohort:
    description: "Patient cohort for clinical analytics"
    path: ./cohorts/patient_definition.json
```

---

## 5. Beyond Fabric: Entra and Azure

The same `udp.yml` can declare resources on two further control planes. Each type
is tagged with a **platform** that selects the provider used to deploy it. Fabric
is the default; the types below opt into Entra or Azure. See the
[Multi-platform guide](multi-platform.md) for the end-to-end walkthrough and the
[`10-azure-and-entra`](https://github.com/microsoft/UDPi30/tree/main/examples/10-azure-and-entra)
example.

| Platform | Deployed via | Scope | Auth |
|---|---|---|---|
| Fabric | Fabric REST API | Workspace | Fabric token |
| Entra | Microsoft Graph | Tenant | Graph `*.ReadWrite.All` |
| Azure | Bicep through `az` CLI | Subscription / resource group | `az login` |

### 5.1 Microsoft Entra

The resource key is the object's `displayName`. Create is idempotent — an existing
object of the same display name is patched, not duplicated.

| Resource | Type Key | Graph Entity |
|----------|----------|--------------|
| Security Group | `entra_groups` | group |
| App Registration | `entra_apps` | application |

```yaml
resources:
  entra_groups:
    sg-analytics-readers:
      description: "Read access to the analytics workload"
      security_enabled: true
      assignable_to_role: false
      owners: []          # display names, UPNs, or object GUIDs
      members: []

  entra_apps:
    analytics-ingest-app:
      sign_in_audience: AzureADMyOrg
      redirect_uris: ["https://localhost/callback"]
      identifier_uris: ["api://analytics-ingest"]
      create_service_principal: true
```

### 5.2 Azure (Bicep)

Resource groups and storage accounts are emitted as generated Bicep; any other
resource type is supplied as an author-written `.bicep` file via
`azure_deployments`. Subscription and location default from the top-level
`azure:` block and can be overridden per resource.

| Resource | Type Key | ARM Type |
|----------|----------|----------|
| Resource Group | `azure_resource_groups` | Microsoft.Resources/resourceGroups |
| Storage Account | `azure_storage_accounts` | Microsoft.Storage/storageAccounts |
| Generic Bicep | `azure_deployments` | Microsoft.Resources/deployments |

In addition, these **first-class service types** each deploy as a single ARM
resource via a generated, resource-group-scope Bicep template. They share the
same shape (`resource_group`, `location`, optional `sku`/`kind`/`properties`/`tags`).
See the full catalogue in [`11-azure-data-services`](https://github.com/microsoft/UDPi30/tree/main/examples/11-azure-data-services).

| Service | Type Key | ARM Type |
|---------|----------|----------|
| Data Factory | `azure_data_factories` | Microsoft.DataFactory/factories |
| Databricks | `azure_databricks_workspaces` | Microsoft.Databricks/workspaces |
| Databricks Structured Streaming | `azure_databricks_structured_streaming` | Microsoft.Databricks/workspaces |
| Event Hubs | `azure_event_hub_namespaces` | Microsoft.EventHub/namespaces |
| Event Grid | `azure_event_grid_topics` | Microsoft.EventGrid/topics |
| Stream Analytics | `azure_stream_analytics_jobs` | Microsoft.StreamAnalytics/streamingjobs |
| IoT Hub | `azure_iot_hubs` | Microsoft.Devices/IotHubs |
| Logic Apps | `azure_logic_apps` | Microsoft.Logic/workflows |
| Functions | `azure_functions` | Microsoft.Web/sites |
| Blob Storage | `azure_blob_storage` | Microsoft.Storage/storageAccounts |
| Data Lake Storage Gen2 | `azure_data_lake_storage` | Microsoft.Storage/storageAccounts |
| Files | `azure_files` | Microsoft.Storage/storageAccounts |
| Queue Storage | `azure_queue_storage` | Microsoft.Storage/storageAccounts |
| Table Storage | `azure_table_storage` | Microsoft.Storage/storageAccounts |
| SQL Database | `azure_sql_databases` | Microsoft.Sql/servers/databases |
| SQL Managed Instance | `azure_sql_managed_instances` | Microsoft.Sql/managedInstances |
| SQL Server on VMs | `azure_sql_virtual_machines` | Microsoft.SqlVirtualMachine/sqlVirtualMachines |
| Database for PostgreSQL | `azure_postgresql` | Microsoft.DBforPostgreSQL/flexibleServers |
| Database for MySQL | `azure_mysql` | Microsoft.DBforMySQL/flexibleServers |
| Database for MariaDB | `azure_mariadb` | Microsoft.DBforMariaDB/servers |
| Cosmos DB | `azure_cosmosdb_accounts` | Microsoft.DocumentDB/databaseAccounts |
| Cache for Redis | `azure_redis_cache` | Microsoft.Cache/Redis |
| Data Box | `azure_data_box` | Microsoft.DataBox/jobs |

**Governance / security / AI**

| Service | Type Key | ARM Type |
|---------|----------|----------|
| Microsoft Purview | `azure_purview_accounts` | Microsoft.Purview/accounts |
| Key Vault | `azure_key_vaults` | Microsoft.KeyVault/vaults |
| Azure Policy | `azure_policy_assignments` | Microsoft.Authorization/policyAssignments |
| Defender for Cloud | `azure_defender_plans` | Microsoft.Security/pricings |
| Machine Learning | `azure_machine_learning_workspaces` | Microsoft.MachineLearningServices/workspaces |
| AI Foundry | `azure_ai_foundry` | Microsoft.MachineLearningServices/workspaces |
| Sentinel | `azure_sentinel` | Microsoft.SecurityInsights/onboardingStates |
| AI Video Indexer | `azure_video_indexer` | Microsoft.VideoIndexer/accounts |
| OpenAI | `azure_openai` | Microsoft.CognitiveServices/accounts |
| Load Testing | `azure_load_testing` | Microsoft.LoadTestService/loadTests |
| Agent identities | `azure_agent_ids` | Microsoft.ManagedIdentity/userAssignedIdentities |
| Service Groups | `azure_service_groups` | Microsoft.Management/serviceGroups |

**Monitoring / observability**

| Service | Type Key | ARM Type |
|---------|----------|----------|
| Monitor (App Insights) | `azure_monitor_components` | Microsoft.Insights/components |
| Alerts | `azure_alerts` | Microsoft.Insights/metricAlerts |
| Diagnostic Settings | `azure_diagnostic_settings` | Microsoft.Insights/diagnosticSettings |
| Log Analytics Workspace | `azure_log_analytics_workspaces` | Microsoft.OperationalInsights/workspaces |
| Metrics (DCR) | `azure_metrics` | Microsoft.Insights/dataCollectionRules |
| Workbooks | `azure_workbooks` | Microsoft.Insights/workbooks |
| Activity Log | `azure_activity_logs` | Microsoft.Insights/diagnosticSettings |
| Database Watchers | `azure_database_watchers` | Microsoft.DatabaseWatcher/watchers |

**Networking**

| Service | Type Key | ARM Type |
|---------|----------|----------|
| Network Watcher | `azure_network_watchers` | Microsoft.Network/networkWatchers |
| DNS Zones | `azure_dns_zones` | Microsoft.Network/dnsZones |
| Network Interfaces | `azure_network_interfaces` | Microsoft.Network/networkInterfaces |
| Private DNS Zones | `azure_private_dns_zones` | Microsoft.Network/privateDnsZones |
| Public IP Address | `azure_public_ip_addresses` | Microsoft.Network/publicIPAddresses |
| Route Tables | `azure_route_tables` | Microsoft.Network/routeTables |
| Virtual Network | `azure_virtual_networks` | Microsoft.Network/virtualNetworks |
| Local Network Gateway | `azure_local_network_gateways` | Microsoft.Network/localNetworkGateways |
| Peering Services | `azure_peering_services` | Microsoft.Network/peeringServices |
| Peering | `azure_peerings` | Microsoft.Peering/peerings |
| Virtual Network Gateway | `azure_virtual_network_gateways` | Microsoft.Network/virtualNetworkGateways |
| Virtual WAN | `azure_virtual_wans` | Microsoft.Network/virtualWans |
| DDoS Protection | `azure_ddos_protection_plans` | Microsoft.Network/ddosProtectionPlans |
| Firewall | `azure_firewalls` | Microsoft.Network/azureFirewalls |
| IP Groups | `azure_ip_groups` | Microsoft.Network/ipGroups |
| Network Security Group | `azure_network_security_groups` | Microsoft.Network/networkSecurityGroups |
| Application Gateway | `azure_application_gateways` | Microsoft.Network/applicationGateways |
| Application Security Group | `azure_application_security_groups` | Microsoft.Network/applicationSecurityGroups |

Catalogue: [`12-azure-platform-services`](https://github.com/microsoft/UDPi30/tree/main/examples/12-azure-platform-services).

!!! note "Storage-family naming & child resources"
    The five storage-family services deploy as storage accounts, so their names
    must be **3–24 lowercase alphanumeric**. Child resources such as
    `azure_sql_databases` may need a parent server — name the database
    `"<server>/<database>"` or use `azure_deployments` for full control.

```yaml
azure:
  subscription: "${var.subscription_id}"
  location: eastus

resources:
  azure_resource_groups:
    rg-analytics-dev:
      location: eastus
      tags: { env: dev }

  azure_storage_accounts:
    saanalyticsdev01:               # 3–24 lowercase alphanumeric
      resource_group: rg-analytics-dev
      sku: Standard_LRS
      kind: StorageV2
      access_tier: Hot

  azure_deployments:
    keyvault-analytics:
      scope: group                  # or "subscription"
      resource_group: rg-analytics-dev
      template_file: ./bicep/keyvault.bicep
      parameters:
        vaultName: kv-analytics-dev
```

!!! note "Workspace-centric orchestration"
    The deploy engine is still workspace-oriented: a deployment containing **only**
    Entra/Azure resources will still create a Fabric workspace. Mixed and
    Fabric-only deployments are unaffected.
