# streaming-lakehouse-governance template

A streaming analytics estate — **Azure Event Hub → Azure Databricks → Fabric
Lakehouse → Data Agent** — with Azure governance baked in and a ready-to-use
**Azure DevOps pipeline** (`azure-pipelines.yml`).

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
Agent**. Azure Key Vault, Policy, Defender, Monitor, and Sentinel govern the
estate.

## Use it

```bash
udpi30 init --template streaming-lakehouse-governance --name my-streaming
cd my-streaming
udpi30 validate
udpi30 plan --target dev
udpi30 deploy --target dev
```

Set `capacity_id`, `subscription_id`, `resource_group`, and `location` during
`init` (or edit `udp.yml`).

## CI/CD

Copy `azure-pipelines.yml` into your Azure DevOps repo, set the
`udp-credentials` variable group (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`,
`AZURE_CLIENT_SECRET`), and point `azureSubscription` at your service connection.
The pipeline validates on PRs, deploys to **staging** on merge to `main`, then to
**production** behind a manual approval gate.

## Prerequisites

- Active Fabric capacity (`capacity_id`)
- `az login` / a service connection with rights on the target subscription
- Appropriate Azure RBAC for the governance resources (e.g. Security Admin for
  Defender/Sentinel onboarding)
