# 11 — Azure data services

A catalogue of every first-class `azure_*` service resource type, deployed in one
run. Each service is emitted as a single ARM resource via a generated,
resource-group-scope Bicep template.

## Services covered

| Group | Type keys |
|-------|-----------|
| Integration / streaming / compute | `azure_data_factories`, `azure_databricks_workspaces`, `azure_databricks_structured_streaming`, `azure_event_hub_namespaces`, `azure_event_grid_topics`, `azure_stream_analytics_jobs`, `azure_iot_hubs`, `azure_logic_apps`, `azure_functions` |
| Storage | `azure_blob_storage`, `azure_data_lake_storage`, `azure_files`, `azure_queue_storage`, `azure_table_storage` |
| Databases | `azure_sql_databases`, `azure_sql_managed_instances`, `azure_sql_virtual_machines`, `azure_postgresql`, `azure_mysql`, `azure_mariadb`, `azure_cosmosdb_accounts`, `azure_redis_cache`, `azure_data_box` |

## Resource shape

Every service shares the same fields:

```yaml
azure_<service>:
  <name>:
    resource_group: rg-...      # required
    subscription: ...           # optional (defaults to azure.subscription)
    location: ...               # optional (defaults to azure.location)
    sku: ...                    # optional → sku: { name: ... }
    kind: ...                   # optional
    properties: { ... }         # ARM properties, emitted verbatim into Bicep
    tags: { ... }
```

## Notes

- **Storage family** (`azure_blob_storage`, `azure_data_lake_storage`,
  `azure_files`, `azure_queue_storage`, `azure_table_storage`) each deploy as a
  storage account, so their resource names must be **3–24 lowercase alphanumeric**
  characters. Differentiate them with `kind`/`properties` (e.g. Data Lake sets
  `isHnsEnabled: true`).
- **Child resources** such as `azure_sql_databases` may require a parent (a SQL
  logical server). For a real deploy, name the database `"<server>/<database>"`
  or use `azure_deployments` with your own template.
- For anything the generic shape doesn't cover, use
  [`azure_deployments`](../10-azure-and-entra/) with an author-supplied `.bicep`.

## Run

```bash
udpi30 validate
udpi30 plan --target dev
udpi30 deploy --target dev
```

See the [Multi-platform guide](../../docs/guide/multi-platform.md).
