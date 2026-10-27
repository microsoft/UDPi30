# Multi-platform resources (Fabric + Entra + Azure)

A single `udp.yml` can declare resources across three control planes. Each
resource type is tagged with a **platform** that selects the provider used to
deploy it:

| Platform | Control plane | Deployed via | Scope |
| --- | --- | --- | --- |
| **Fabric** | Microsoft Fabric items | Fabric REST API | Workspace |
| **Entra** | Directory objects | Microsoft Graph | Tenant |
| **Azure** | ARM resources | Bicep through the `az` CLI | Subscription / resource group |

Fabric remains the default — every existing resource type is a Fabric item and
needs no extra configuration. Entra and Azure types are addressed by their own
top-level resource keys.

See the worked example in [`examples/10-azure-and-entra`](https://github.com/microsoft/UDPi30/tree/main/examples/10-azure-and-entra).

## Microsoft Entra

The resource key is the object's `displayName`. Create is idempotent — an
existing object with the same display name is patched, not duplicated.

```yaml
resources:
  entra_groups:
    sg-analytics-readers:
      description: "Read access to the analytics workload"
      security_enabled: true
      members: []            # display names, UPNs, or object GUIDs

  entra_apps:
    analytics-ingest-app:
      sign_in_audience: AzureADMyOrg
      create_service_principal: true
```

**Permissions:** the deploying identity needs Microsoft Graph
`Group.ReadWrite.All` and/or `Application.ReadWrite.All`.

## Azure (Bicep)

Azure resources are deployed as Bicep. Resource groups and storage accounts are
emitted as generated Bicep templates; anything else is supplied as an
author-written `.bicep` file via `azure_deployments`. Subscription and location
default from the top-level `azure:` block and can be overridden per resource.

```yaml
azure:
  subscription: "${var.subscription_id}"
  location: eastus

resources:
  azure_resource_groups:
    rg-analytics-dev:
      location: eastus
      tags: { env: dev }

  azure_storage_accounts:
    saanalyticsdev01:               # 3–24 lowercase alphanumeric
      resource_group: rg-analytics-dev
      sku: Standard_LRS
      kind: StorageV2

  azure_deployments:                # generic escape hatch — any Bicep template
    keyvault-analytics:
      scope: group
      resource_group: rg-analytics-dev
      template_file: ./bicep/keyvault.bicep
      parameters:
        vaultName: kv-analytics-dev
```

**Prerequisites:** `az login` with rights on the target subscription. The
provider uses `az deployment {sub,group} create --template-file *.bicep`, which
compiles Bicep in-process — no separate Bicep toolchain is required.

## Naming rules

Fabric item-name character rules are **not** applied to Entra or Azure names —
those platforms validate server-side. The one client-side check kept is for
storage account names (3–24 lowercase alphanumeric), which fail fast.

## Known limitation

The deploy orchestration is still workspace-centric: a deployment that contains
**only** Entra/Azure resources will still create a Fabric workspace. Mixed
deployments (the intended use) and Fabric-only deployments are unaffected.
Gating workspace creation when no Fabric items are present is tracked as
follow-up work.
