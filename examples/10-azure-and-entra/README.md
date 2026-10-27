# 10 — Azure & Entra (cross-plane)

One `udp.yml` that deploys across **three control planes** in a single run:

- **Fabric** (`lakehouses`) — via the Fabric REST API, into the workspace
- **Entra** (`entra_groups`, `entra_apps`) — via Microsoft Graph, at tenant scope
- **Azure** (`azure_resource_groups`, `azure_storage_accounts`, `azure_deployments`)
  — via Bicep through the `az` CLI

Each resource type is tagged with the platform that owns it; the deploy engine
routes each item to the matching provider.

## Files

| File | Purpose |
|------|---------|
| `udp.yml` | The cross-plane deployment definition |
| `bicep/keyvault.bicep` | Sample author-supplied template used by `azure_deployments` |

## Prerequisites

| Plane | Requirement |
|-------|-------------|
| Fabric | An active capacity GUID (`var.capacity_id`) |
| Entra | Graph `Group.ReadWrite.All` and/or `Application.ReadWrite.All` for the deploying identity |
| Azure | `az login` with rights on the target subscription (`var.subscription_id`) |

## Run

```bash
udpi30 validate
udpi30 plan --target dev
udpi30 deploy --target dev
```

Set `capacity_id` and `subscription_id` (in `udp.yml` `variables` or via
`-V capacity_id=… -V subscription_id=…`) before deploying.

## Notes

- Entra create is idempotent — an existing group/app with the same display name
  is patched, not duplicated.
- Azure resources deploy with `az deployment {sub,group} create --template-file
  *.bicep`, which compiles Bicep in-process (no separate Bicep toolchain needed).
- Storage account names must be 3–24 lowercase alphanumeric characters.

See the [Multi-platform guide](../../docs/guide/multi-platform.md) for details.
