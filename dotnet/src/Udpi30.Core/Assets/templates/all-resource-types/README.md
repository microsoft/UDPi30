# ${{ project_name }}

Generated from the **all-resource-types** template — a reference catalogue whose
`udp.yml` declares **all 46 supported Fabric item types**, cross-referenced so the
dependency graph is exercised (notebooks → environment + lakehouse, reports →
semantic model, KQL database → eventhouse, agents → sources, and so on).

Treat this as a **copy-paste catalogue, not a starter project** — most real
deployments use only a handful of these types. Delete the blocks you don't need.

## Quick start

```bash
udpi30 validate          # validates offline (no capacity required)
# set workspace.capacity_id (and target capacity_id) in udp.yml
udpi30 plan --target dev
udpi30 deploy --target dev
```

## Before you deploy

`validate` passes out of the box, but a real `deploy` needs more than the
`udp.yml`:

- **Set your capacity.** Fill in `capacity_id` (workspace and/or per target). Find
  it in the Fabric Admin Portal → Capacities.
- **Supply definition files** for items that require them. This template includes
  working stubs for notebooks, the Spark job, SQL, KQL, and the Data Agent, but
  leaves placeholders for items that need exported definitions:
  - Semantic models (`./semantic_model/`) — TMDL files from Power BI Desktop
  - Reports (`./reports/sales/`) — PBIR files
  - Reflex, dataflows, digital twins, graphs, maps, etc. — supply their definitions
- **Mind capacity-gated / config-required types.** A few types (dbt jobs, event
  schema sets, HLS cohorts, Snowflake/Cosmos/Databricks mirrors, digital twin
  flows) require specific capacity features or external connections. See the
  [Resource Types guide](https://microsoft.github.io/UDPi30/guide/resource-types/)
  and the project README's "Tested Item Types" table for current status.

Deploy incrementally — start with the resources you need and add the rest as you
wire up their definitions.

## Files

| Path | Used by |
|------|---------|
| `notebooks/ingest.py` | `notebooks.ingest_notebook` |
| `spark/batch_job.py` | `spark_job_definitions.batch_job` |
| `sql/create_views.sql` | `warehouses.analytics_warehouse` |
| `sql/app_schema.sql` | `sql_databases.app_db` |
| `kql/create_tables.kql` | `eventhouses.telemetry_eventhouse` |
| `agent/instructions.md`, `agent/examples.yaml` | `data_agents.analytics_agent` |
| `agent/ops_instructions.md` | `operations_agents.ops_agent` |
