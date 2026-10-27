# Templates

This page documents project templates: starter projects that generate a complete `udp.yml`, directory structure, and sample definition files. Templates remove boilerplate and produce a working project in seconds.

---

## 1. Using templates

```bash
udpi30 init --template <template_name> --name <project_name>
```

This creates a new directory with the project name containing a fully configured deployment. Built-in templates ship with the tool in `Assets/templates/`.

---

## 2. Built-in templates

| Template | Description |
|----------|-------------|
| `blank` | Minimal starting point: empty `udp.yml` plus the standard directory structure. |
| `medallion` | Bronze/Silver/Gold lakehouse architecture with ETL notebooks, a data pipeline, and a Data Agent. |
| `all-resource-types` | Reference catalogue: a `udp.yml` that declares all 46 supported Fabric item types. |
| `realtime-governance` | Real-time intelligence (IoT → Eventhouse → Eventstream → Activator) with a lakehouse, Data Agent, and materialized lake views, governed by Azure Key Vault, Policy, Defender, Monitor, and Sentinel — ships an Azure DevOps pipeline. |
| `streaming-lakehouse-governance` | Streaming analytics: Azure Event Hub → Azure Databricks → Fabric Lakehouse + Data Agent, governed by Azure Key Vault, Policy, Defender, Monitor, and Sentinel — ships an Azure DevOps pipeline. |

### 2.1 `blank`

A minimal starting point with an empty `udp.yml` and the standard directory structure.

```bash
udpi30 init --template blank --name udp-project
```

Creates:

```
udp-project/
├── udp.yml
├── notebooks/
├── sql/
├── pipelines/
├── agent/
└── .gitignore
```

The generated `udp.yml` contains the `deployment`, `workspace`, `variables`, `resources`, and `targets` sections with placeholder comments. Add your own resources and fill in the workspace details.

### 2.2 `medallion`

A Bronze/Silver/Gold lakehouse architecture with ETL notebooks, data pipelines, and a Data Agent. This is the most common pattern for data engineering projects on Fabric.

```bash
udpi30 init --template medallion --name contoso-analytics
```

Creates:

```
contoso-analytics/
├── udp.yml
├── notebooks/
│   ├── ingest_to_bronze.py
│   ├── bronze_to_silver.py
│   └── silver_to_gold.py
├── sql/
│   └── create_gold_views.sql
├── semantic_model/          # TMDL definition for analytics_model
│   ├── definition.pbism
│   └── definition/...
├── reports/dashboard/       # PBIR definition for the report
│   ├── definition.pbir
│   └── definition/...
├── agent/
│   ├── instructions.md
│   └── examples.yaml
└── .gitignore
```

The template now ships a minimal **TMDL semantic model** and **PBIR report** so the `analytics_model` and `dashboard` items deploy end-to-end. The semantic model is a self-contained starter (a single calculated table) — replace it with your real model (for example, a Direct Lake model over the gold lakehouse). On deploy, udpi30 automatically rebinds the report to the deployed semantic model's id (the Fabric REST API requires a `byConnection` reference).

Resources defined in `udp.yml`:

| Resource | Type | Description |
|----------|------|-------------|
| `bronze` | Lakehouse | Raw data landing zone |
| `silver` | Lakehouse | Cleansed and conformed data |
| `gold` | Lakehouse | Business-ready aggregates |
| `ingest_to_bronze` | Notebook | Ingests raw data into bronze |
| `bronze_to_silver` | Notebook | Transforms bronze to silver |
| `silver_to_gold` | Notebook | Aggregates silver into gold |
| `daily_ingest` | Data Pipeline | Orchestrates the ETL flow |
| `analytics_agent` | Data Agent | Natural language query agent over gold |
| `spark_env` | Environment | Spark runtime with library dependencies |

The template includes dev and prod targets with variable overrides for database connections.

### 2.3 `all-resource-types`

A reference catalogue whose `udp.yml` declares **all 46 supported Fabric item types**, cross-referenced so the dependency graph is exercised. Use it to copy the exact schema for any item type — most projects keep only a handful and delete the rest.

```bash
udpi30 init --template all-resource-types --name udp-catalogue
```

Creates a project with working stubs for the deployable text-based items (notebooks, Spark job, SQL, KQL, Data Agent) and placeholders for items that need exported definitions (semantic model TMDL, report PBIR, etc.):

```
udp-catalogue/
├── udp.yml                 # all 46 item types
├── notebooks/ingest.py
├── spark/batch_job.py
├── sql/create_views.sql
├── sql/app_schema.sql
├── kql/create_tables.kql
├── agent/instructions.md
├── agent/ops_instructions.md
├── agent/examples.yaml
├── README.md
└── .gitignore
```

The generated `udp.yml` **validates out of the box**. To `deploy`, set your `capacity_id` and supply the definition files for definition-required items — see the generated `README.md` and the [Resource Types guide](resource-types.md). Deploy incrementally; some types are capacity-gated or need external connections.

### 2.4 `realtime-governance`

A cross-platform real-time intelligence estate with Azure governance and a ready-made **Azure DevOps pipeline** (`azure-pipelines.yml`).

```bash
udpi30 init --template realtime-governance --name my-realtime
```

Telemetry flows **IoT Hub → Eventstream → Eventhouse**; a curated **Lakehouse** feeds **Materialized Lake Views** and a **Data Agent**, while a **Reflex** activator watches for anomalies. The Azure governance resources wrap the estate:

| Resource | Type | Plane |
|----------|------|-------|
| `iot-telemetry-hub` | IoT Hub | Azure |
| `telemetry_eventhouse` | Eventhouse | Fabric |
| `device_stream` | Eventstream | Fabric |
| `anomaly_activator` | Real-Time Activator (Reflex) | Fabric |
| `telemetry_lakehouse` | Lakehouse | Fabric |
| `device_daily_summary` | Materialized Lake View | Fabric |
| `telemetry_agent` | Data Agent | Fabric |
| `kv-realtime` | Key Vault | Azure |
| `require-resource-tags` | Policy | Azure |
| `defender-servers` | Defender for Cloud | Azure |
| `appi-realtime` | Monitor (App Insights) | Azure |
| `sentinel-onboard` | Microsoft Sentinel | Azure |

The bundled `azure-pipelines.yml` validates on PRs, deploys to **staging** on merge to `main`, then to **production** behind a manual approval gate. Create a variable group `udp-credentials` (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`).

### 2.5 `streaming-lakehouse-governance`

A streaming analytics estate — **Azure Event Hub → Azure Databricks → Fabric Lakehouse → Data Agent** — with Azure governance and a bundled **Azure DevOps pipeline** (`azure-pipelines.yml`).

```bash
udpi30 init --template streaming-lakehouse-governance --name my-streaming
```

Events land in **Event Hub**, are processed by **Azure Databricks**, and the curated results are stored in a **Fabric Lakehouse** exposed through a **Data Agent**:

| Resource | Type | Plane |
|----------|------|-------|
| `ehns-streaming` | Event Hub | Azure |
| `dbw-streaming` | Databricks | Azure |
| `streaming_lakehouse` | Lakehouse | Fabric |
| `streaming_agent` | Data Agent | Fabric |
| `kv-streaming` | Key Vault | Azure |
| `require-resource-tags` | Policy | Azure |
| `defender-servers` | Defender for Cloud | Azure |
| `appi-streaming` | Monitor (App Insights) | Azure |
| `sentinel-onboard` | Microsoft Sentinel | Azure |

Same pipeline shape as `realtime-governance`: validate on PRs → staging on merge → prod behind an approval gate, driven by the `udp-credentials` variable group.

---

## 3. Creating custom templates

A custom template is a directory containing a `template.yml` manifest and a set of files that are copied into the new project. File contents can include `${{ variable }}` placeholders that are substituted at init time.

### 3.1 Directory structure

```
udp-custom-template/
├── template.yml
├── udp.yml
├── notebooks/
│   └── setup.py
├── sql/
│   └── init.sql
└── .gitignore
```

### 3.2 `template.yml` format

```yaml
name: udp-custom-template
description: "A custom template for our team's standard project layout."
version: "1.0.0"
author: "Data Platform Team"

variables:
  project_name:
    description: "Project name (used in resource naming)"
    default: "udp-project"

  lakehouse_prefix:
    description: "Prefix for lakehouse names"
    default: "bronze"

  capacity_id:
    description: "Fabric capacity GUID for the dev target"
    default: ""
```

When a user runs `udpi30 init --template ./udp-custom-template`, the defaults from `template.yml` are applied unless overridden with `--var KEY=VALUE`.

### 3.3 Placeholders in template files

Template files use simple `${{ variable }}` placeholder substitution. There is no conditional or loop syntax; a template renders the same structure every time, parameterized by variable values. A placeholder with no matching variable renders as an empty string.

| Rule | Behavior |
|------|----------|
| Substitution syntax | `${{ variable_name }}` (whitespace inside the braces is allowed) |
| Rendered file types | `.yml`, `.yaml`, `.py`, `.md`, `.txt`, `.json`, `.sql`, `.kql` |
| Other file types | Copied verbatim, no substitution |
| Unknown variables | Replaced with an empty string |

In `udp.yml`:

```yaml
deployment:
  name: ${{project_name}}
  version: "1.0.0"

resources:
  lakehouses:
    ${{lakehouse_prefix}}_landing:
      display_name: "${{project_name}}_landing"
      description: "Raw data landing zone"

targets:
  dev:
    default: true
    workspace:
      name: ${{project_name}}-dev
      capacity_id: "${{capacity_id}}"
```

### 3.4 Built-in template variables

One variable is always available in addition to the ones defined in `template.yml`:

| Variable | Description |
|----------|-------------|
| `project_name` | The `--name` value passed to `udpi30 init` |

---

## 4. Remote templates

Templates can be loaded from a URL or a GitHub repository. This allows teams to share standard templates without copying files.

### 4.1 URL-based templates

Point to a `.tar.gz` archive containing the template directory (`.zip` archives are not supported):

```bash
udpi30 init --template https://example.com/templates/data-mesh-v2.tar.gz --name udp-project
```

### 4.2 GitHub shorthand

Use the `github:owner/repo` prefix to reference a template repository. The shorthand downloads the `main` branch as a tarball; subdirectory paths and branch/tag pins are not supported:

```bash
udpi30 init --template github:contoso/udp-templates --name udp-project
```

The archive must contain a `template.yml`; the directory containing the first `template.yml` found is used as the template root. Only publicly accessible URLs and repositories are supported.

Note: remote templates (URL and `github:`) are copied verbatim. `${{ variable }}` placeholder rendering currently applies to built-in and local directory templates only.

---

## 5. Template variables at init time

When running `udpi30 init`, variables are resolved in this order:

1. **Command-line flags**: `--var project_name=udp-project`
2. **Default values**: From the `variables` section of `template.yml`.
3. **Unmatched placeholders**: Render as empty strings; `udpi30 validate` will surface any resulting gaps.

```bash
# Provide all variables on the command line (no prompts)
udpi30 init \
  --template medallion \
  --name contoso-analytics \
  --var lakehouse_prefix=contoso \
  --var include_agent=true
```
