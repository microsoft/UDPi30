# 15 — Unified Data Platform all-up

One `udp.yml` covering every box in the *Unified Data Platform – Conceptual Model* diagram: Data Sources → Ingest → Store → Process → Serve → Enrich → Consumers, plus the OneLake, Orchestrate & Deploy, Secure & Connect, Platform Optics, Microsoft Purview and Microsoft Azure layers.

Each section of [udp.yml](./udp.yml) has a `Diagram:` comment naming the boxes it implements. Boxes that udpi30 cannot declare (domains, gateways, Purview data plane, Conditional Access, and others) are listed at the bottom of the file.

| Diagram area                                                                | udp.yml                                                                                                                                     |
| --------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Batch sources, Shortcut                                                     | `lakehouses.bronze_lakehouse.shortcuts`                                                                                                   |
| Fabric Mirror, Mirror Data                                                  | `mirrored_databases.operational_mirror`                                                                                                   |
| Eventstream, Eventhouse, KQL Update Policy                                  | `eventstreams`, `eventhouses`, `kql_databases`                                                                                        |
| Notebook, Pipeline, Copy Job, Dataflow Gen2, UDF, MLV, Spark Job            | `notebooks`, `pipelines`, `copy_jobs`, `dataflows`, `user_data_functions`, `materialized_lake_views`, `spark_job_definitions` |
| Lakehouse, Warehouse, SQL Database                                          | `lakehouses`, `warehouses`, `sql_databases`                                                                                           |
| Semantic Model, Power BI, GraphQL, Real-Time Dashboard, Activator           | `semantic_models`, `reports`, `graphql_apis`, `kql_dashboards`, `reflex`                                                          |
| Data Agent, Ontology, ML Models, Copilot                                    | `data_agents`, `ontologies`, `ml_models`, `admin` (`EnableAOAI`)                                                                  |
| Consumers, Entra ID                                                         | `entra_apps`, `entra_groups`, `azure_ai_foundry`                                                                                      |
| OneLake security, Workspace roles                                           | `security.roles` (with `onelake_roles`)                                                                                                 |
| Git, Variable Library, Environment, Airflow, Deployment Pipelines           | `workspace.git_integration`, `variable_libraries`, `environments`, `airflow_jobs`, `targets`                                      |
| Private Link, VNet, Key Vault, Monitor, Sentinel, Defender, Policy, Purview | `azure_*` resources and `admin` tenant settings                                                                                         |

## Use it

```bash
cd examples/15-udp-allup
udpi30 validate --skip-connection-check --strict
udpi30 plan -t dev
```

Set `capacity_id` (and the other `variables:` defaults) before deploying. Do not run `udpi30 admin apply` without reviewing `udpi30 admin plan`: the `admin:` block changes tenant-wide settings.

This example is not part of the 14-example deployment test harness.
