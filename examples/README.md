# Examples

Working examples you can copy and deploy to your Fabric tenant.

To validate or deploy the complete catalogue through the manual GitHub Actions
and Azure DevOps pipelines, see the [deployment example test harness](../deployment-tests/README.md).

## Quick Start

1. Copy any example folder to your working directory
2. Update `capacity_id` in `udp.yml` with your Fabric capacity GUID
3. Run:

   ```bash
   udpi30 validate
   udpi30 plan --target dev
   udpi30 deploy --target dev
   ```

## Example Catalogue

| # | Example | Description | Complexity |
| --- | --- | --- | --- |
| 00 | [Default (complete reference)](00-default/) | Every section, resource type (Fabric, Entra, Azure), tenant setting, security and Purview setting in one `udp.yml` | Reference |
| 01 | [Minimal](01-minimal/) | One lakehouse, one notebook | Beginner |
| 02 | [Medallion Lakehouse](02-medallion-lakehouse/) | Bronze/Silver/Gold ETL with scheduling | Intermediate |
| 03 | [Real-Time Intelligence](03-real-time-intelligence/) | Eventhouse + Eventstream + KQL | Intermediate |
| 04 | [Data Science](04-data-science/) | ML experiments, training pipelines, Spark jobs | Intermediate |
| 05 | [Multi-Environment](05-multi-environment/) | Policies, secrets, canary deploy, notifications | Advanced |
| 06 | [Shortcuts & Connections](06-shortcuts-and-connections/) | ADLS, S3, cross-workspace shortcuts | Intermediate |
| 07 | [Shortcut Transformations](07-shortcut-transformations/) | CSV/JSON/Excel → Delta, AI summarize/translate/classify | Intermediate |
| 08 | [All Resource Types](08-all-resource-types/) | Reference catalogue using all 45 supported item types | Reference |
| 09 | [Tenant Settings](09-tenant-settings/) | Declarative org-wide Fabric admin/tenant settings | Advanced |
| 10 | [Azure & Entra](10-azure-and-entra/) | Cross-plane: Fabric + Entra groups/apps + Azure resources via Bicep | Advanced |
| 11 | [Azure Data Services](11-azure-data-services/) | Catalogue of all `azure_*` service types (Data Factory, Databricks, Event Hubs, SQL, Cosmos DB, …) | Reference |
| 12 | [Azure Platform Services](12-azure-platform-services/) | Governance, security, AI, monitoring & networking `azure_*` types (Key Vault, Purview, Sentinel, VNet, Firewall, …) | Reference |
| 13 | [Real-Time Governance](13-realtime-governance/) | IoT → Eventhouse → Eventstream + Activator, Lakehouse, Data Agent, Materialized Lake Views & Azure governance | Advanced |
| 14 | [Streaming Lakehouse Governance](14-streaming-lakehouse-governance/) | Event Hub → Databricks → Lakehouse + Data Agent, with Azure Key Vault, Policy, Defender, Monitor & Sentinel | Advanced |
| 15 | [Unified Data Platform All-Up](15-udp-allup/) | Every box of the UDP conceptual model: ingest, store, process, serve, enrich, consumers, security, governance and operations | Reference |

## Finding Your Capacity GUID

```bash
az rest --method get \
  --url "https://api.fabric.microsoft.com/v1/capacities" \
  --resource "https://api.fabric.microsoft.com"
```

## Need Help?

- [Documentation](https://microsoft.github.io/UDPi30/)
- [GitHub Issues](https://github.com/microsoft/UDPi30/issues)
- `udpi30 diag` to diagnose common issues
