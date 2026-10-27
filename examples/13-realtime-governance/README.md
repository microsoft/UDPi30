# 13 — Real-time intelligence + Azure governance

The concrete form of the [`realtime-governance`](../../dotnet/src/Udpi30.Core/Assets/templates/realtime-governance/) template — a cross-plane real-time estate with Azure governance and a ready-made Azure DevOps pipeline.

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

Telemetry flows **IoT Hub → Eventstream → Eventhouse**; the **Lakehouse** feeds
the `device_daily_summary` **materialized lake view** and a **Data Agent**, while
a **Reflex** activator watches for anomalies.

## Run

```bash
udpi30 validate
udpi30 plan --target dev
udpi30 deploy --target dev
```

Set `capacity_id` and `subscription_id` first. To scaffold a fresh copy with the
Azure DevOps pipeline included:

```bash
udpi30 init --template realtime-governance --name my-realtime
```
