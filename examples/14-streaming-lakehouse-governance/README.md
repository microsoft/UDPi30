# 14 — Streaming lakehouse + Azure governance

The concrete form of the [`streaming-lakehouse-governance`](../../dotnet/src/Udpi30.Core/Assets/templates/streaming-lakehouse-governance/) template — a streaming analytics estate with Azure governance and a ready-made Azure DevOps pipeline.

## What it deploys

| Plane | Resource | Type |
|-------|----------|------|
| Azure | Event Hub | `azure_event_hub_namespaces` |
| Azure | Databricks | `azure_databricks_workspaces` |
| Fabric | Lakehouse | `lakehouses` |
| Fabric | Data Agent | `data_agents` |
| Azure | Key Vault | `azure_key_vaults` |
| Azure | Policy | `azure_policy_assignments` |
| Azure | Defender for Cloud | `azure_defender_plans` |
| Azure | Monitor (App Insights) | `azure_monitor_components` |
| Azure | Microsoft Sentinel | `azure_sentinel` |

Events land in **Event Hub**, are processed by **Azure Databricks**, and the
curated results are stored in a **Fabric Lakehouse** exposed through a **Data
Agent** — governed by Azure Key Vault, Policy, Defender, Monitor, and Sentinel.

## Run

```bash
udpi30 validate
udpi30 plan --target dev
udpi30 deploy --target dev
```

Set `capacity_id` and `subscription_id` first. To scaffold a fresh copy with the
Azure DevOps pipeline included:

```bash
udpi30 init --template streaming-lakehouse-governance --name my-streaming
```
