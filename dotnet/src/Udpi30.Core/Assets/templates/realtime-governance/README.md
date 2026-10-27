# realtime-governance template

A cross-platform real-time intelligence estate with Azure governance baked in,
plus a ready-to-use **Azure DevOps pipeline** (`azure-pipelines.yml`).

## What it deploys

| Plane | Resource | Type |
|-------|----------|------|
| Azure | IoT Hub | `azure_iot_hubs` |
| Fabric | Eventhouse | `eventhouses` |
| Fabric | Eventstream | `eventstreams` |
| Fabric | Real-Time Activator (Reflex) | `reflex` |
| Fabric | Lakehouse | `lakehouses` |
| Fabric | Data Agent | `data_agents` |
| Fabric | Materialized Lake Views | `materialized_lake_views` |
| Azure | Key Vault | `azure_key_vaults` |
| Azure | Policy | `azure_policy_assignments` |
| Azure | Defender for Cloud | `azure_defender_plans` |
| Azure | Monitor (App Insights) | `azure_monitor_components` |
| Azure | Microsoft Sentinel | `azure_sentinel` |

Telemetry flows **IoT Hub → Eventstream → Eventhouse**, with a curated
**Lakehouse** feeding **Materialized Lake Views** and a **Data Agent**, while a
**Reflex** activator watches for anomalies. The Azure governance resources
(Key Vault, Policy, Defender, Monitor, Sentinel) wrap the estate.

## Use it

```bash
udpi30 init --template realtime-governance --name my-realtime
cd my-realtime
udpi30 validate
udpi30 plan --target dev
udpi30 deploy --target dev
```

Set `capacity_id`, `subscription_id`, `resource_group`, and `location` during
`init` (or edit `udp.yml`).

## CI/CD

Copy `azure-pipelines.yml` into your Azure DevOps repo and create a variable
group `udp-credentials` with `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, and
`AZURE_CLIENT_SECRET`. The pipeline validates on PRs, deploys to **staging** on
merge to `main`, then to **production** behind a manual approval gate.

## Prerequisites

- Active Fabric capacity (`capacity_id`)
- `az login` / a service connection with rights on the target subscription
- For the governance resources, appropriate Azure RBAC (e.g. Security Admin for
  Defender/Sentinel onboarding)
