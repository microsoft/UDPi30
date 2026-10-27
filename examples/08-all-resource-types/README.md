# All Resource Types

A reference deployment that exercises **every one of the 45 supported Fabric item types** in a single `udp.yml`. It validates end-to-end and demonstrates how the types reference each other (notebooks → environment + lakehouse, reports → semantic model, KQL database → eventhouse, agents → sources, and so on).

Treat this as a **copy-paste catalogue**, not a starting template — most real projects use only a handful of these types. Pick the blocks you need.

## Use it

```bash
# 1. Set your capacity GUID (replace the placeholder in udp.yml)
# 2. Validate the whole catalogue
udpi30 validate -f udp.yml

# 3. Preview against a workspace
udpi30 plan --target dev
```

> **Note:** This example references definition paths (notebooks, SQL, TMDL/PBIR, KQL, agent files) that are not all included here — it is built to **validate** and illustrate structure. To actually `deploy`, supply the referenced files, and be aware some types are capacity-gated or require extra configuration (see the table below and the [Tested Item Types](../../README.md#tested-item-types) section).

## What's covered

All 45 types, grouped by workload:

| Workload | Types |
|----------|-------|
| Data Engineering | lakehouses, notebooks, environments, spark_job_definitions, graphql_apis, user_data_functions |
| Data Factory | pipelines, copy_jobs, airflow_jobs, mounted_data_factories, dbt_jobs |
| Data Warehouse | warehouses, sql_databases, mirrored_databases, mirrored_warehouses, snowflake_databases, cosmosdb_databases, mirrored_databricks_catalogs, datamarts |
| Power BI | semantic_models, reports, paginated_reports, dashboards, dataflows |
| Data Science | ml_models, ml_experiments |
| Real-Time Intelligence | eventhouses, kql_databases, eventstreams, kql_querysets, kql_dashboards, reflex, digital_twin_builders, digital_twin_builder_flows, event_schema_sets, graph_query_sets |
| AI & Knowledge | data_agents, operations_agents, anomaly_detectors, ontologies |
| Graph & Spatial | graphs, graph_models, map_items, hls_cohorts |
| Shared config | variable_libraries |

## Naming rule reminder

Strict-naming types — `lakehouses`, `warehouses`, `eventhouses`, `kql_databases`, `sql_databases` — allow only letters, numbers, and underscores. Hyphens and spaces are rejected. This example uses underscores everywhere to stay safe.
