# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.15.0] - 2026-10-26

### Changed

- **Breaking: the project is renamed from `udp-cicd` to `udpi30`.** The
  repository is now [microsoft/UDPi30](https://github.com/microsoft/UDPi30) and the
  documentation site is published at https://microsoft.github.io/UDPi30/.
  - NuGet packages and .NET tool commands: `udp-cicd` is now `udpi30` and
    `udp-cicd-mcp` is now `udpi30-mcp`.
  - .NET projects, assemblies, and namespaces: `UdpCicd.*` is now `Udpi30.*`
    (`Udpi30.Core`, `Udpi30.Cli`, `Udpi30.Mcp`, `Udpi30.Editor`,
    `Udpi30.Ontology`), and the solution is `dotnet/udpi30.slnx`.
  - Local state, audit, and metrics folder: `.udp-cicd` is now `.udpi30`.
  - Pipeline variable in the deployment example pipelines: `UDP_CICD_DLL` is now `UDPI30_DLL`.
  - MCP server registration name: `udp-cicd` is now `udpi30`.
  - Release assets: `udp-cicd-ontology-win-x64.zip` is now
    `udpi30-ontology-win-x64.zip`.
  See [Upgrading from udp-cicd](docs/getting-started/installation.md#7-upgrading-from-udp-cicd)
  for migration steps.
- Documentation, README, schema `id`, and package metadata now point to
  `microsoft/UDPi30`.

### Added

- `scripts/Get-UdpYmlValues.ps1 -ProvisionServicePrincipal` creates the `sp-udpi30`
  service principal when it does not exist and grants the Graph, Azure RBAC, Fabric
  workspace, Fabric tenant-access, state-storage, and Key Vault permissions udpi30
  needs. It is idempotent, supports `-WhatIf`, and reports anything it cannot grant as a
  manual step.
- New example [`examples/00-default`](examples/00-default): a complete `udp.yml`
  reference covering every section and field, all 46 Fabric item types, Entra
  and Azure resources, all 178 Fabric tenant settings, Microsoft Purview and
  information-protection settings, security roles, connections, policies,
  notifications, state backends, and four targets. It validates with `--strict`
  and ships stub assets plus two Bicep templates. It is intentionally not part of
  the 14-example deployment test harness.

## [1.14.0] - 2026-07-31

### Added

- **Deployment example test harness** for all 14 examples, with `Preflight`,
  `Validate`, `Plan`, and `Deploy` modes; isolated resource naming; ownership
  receipts; fail-closed cleanup; structured evidence; and Pester coverage.
- **Manual GitHub Actions and Azure DevOps pipelines** that build the triggering
  commit, use federated Azure authentication for cloud operations, restrict
  mutation to protected `main`, and publish diagnostic evidence on every run.
- **UDPI30 Ontology Builder**, a Windows Forms application that imports a
  Microsoft Fabric semantic model, generates editable ontology entities,
  properties, relationships, logical names, and descriptions, and publishes the
  resulting Ontology item back to Fabric.

### Changed

- Strict manifest loading now defers valid `${secret.*}` and
  `${keyvault.*.*}` references until plan or deployment while continuing to
  reject unresolved ordinary variables.
- Corrected the multi-environment and shortcuts examples so strict validation
  uses the supported variable, secret, connection type, and authentication
  fields.

### Fixed

- Fabric long-running operations now poll the trusted Fabric API origin and
  retrieve the operation result before consuming created items or semantic
  model definitions.
- Ontology generation omits unsupported self-relationships, disambiguates
  cross-entity property names whose value types conflict, and validates edited
  ontology files before publication.

### Security

- Deployment cleanup now requires provider-native IDs, run markers, and exact
  child-resource ownership evidence before deleting Fabric, Azure, or Entra
  resources.
- Tenant-setting tests capture the live pre-mutation values privately and
  require exact post-test convergence before reporting success.
- Release publication is tag-only, verifies the tag against the package
  version, separates credential-free build and validation from publication,
  validates package metadata and every release archive, stages a recoverable
  draft release, and uses the preinstalled GitHub CLI instead of a mutable
  third-party release action.
- GitHub deployment jobs consume a prebuilt CLI artifact and perform no package
  installation, restore, or compilation while OIDC permission is available.

## [1.13.0] - 2026-06-18

### Added

- **Multi-platform reverse import** — `generate` can now reverse-engineer the
  whole deployed footprint, not just Fabric workspace items:
  - **`udpi30 generate`** gained `--include-entra` (imports deployed Microsoft
    Entra security groups and app registrations via Microsoft Graph) and
    `--include-azure` (imports deployed Azure resources via the `az` CLI, mapped
    to their `azure_*` field by ARM type). Azure discovery can be scoped with
    `--subscription` and `--resource-group`, and `--location` records the default
    region in the generated `azure:` block.
  - **`udp_generate` MCP tool** gained matching `include_entra`, `include_azure`,
    `subscription`, and `resource_group` parameters.
  - **Editor — Tools ▸ Import from deployed environment…** (`Ctrl+I`): a new
    dialog connects to the live Fabric workspace, Entra, and/or Azure
    subscription, lists what is deployed, and lets you check which resources to
    reverse-generate into the open `udp.yml` (skipping keys already present).
  - New shared **`ReverseDiscovery`** engine powers all three surfaces, so the
    CLI, MCP server, and Editor produce byte-for-byte identical imported models.
- **`09-tenant-settings` example** expanded to exercise every tenant-settings
  field — all delegation overrides and all five property types (`FreeText`,
  `Url`, `Boolean`, `MailEnabledSecurityGroup`, `Integer`).

## [1.12.0] - 2026-06-15

### Changed

- **Documentation refresh** to bring every page in line with the multi-platform
  feature set:
  - **Editor guide** (`guide/editor.md`) now documents the `Azure (defaults)`
    node, platform-grouped Resources tree, and the platform filter in the Add
    Resource dialog; coverage statement updated to all platforms (46 Fabric +
    Entra + 64 Azure).
  - **Quickstart** lists all five templates (was two).
  - Migration guides (Terraform, fabric-cicd) and the launch post updated to
    46 Fabric item types + Entra + 64 Azure; README preview note de-versioned.

## [1.11.0] - 2026-06-15

### Added

- **`streaming-lakehouse-governance` template** — a streaming analytics estate
  (Azure Event Hub → Azure Databricks → Fabric Lakehouse → Data Agent) governed
  by Azure Key Vault, Policy, Defender for Cloud, Monitor, and Sentinel —
  **ships an Azure DevOps pipeline** (`azure-pipelines.yml`) with validate →
  staging → prod approval-gated stages. New example
  [`examples/14-streaming-lakehouse-governance`](examples/14-streaming-lakehouse-governance).

### Changed

- **udp.yml Editor** now fully supports every platform:
  - **Round-trip fix:** the top-level `azure:` block (subscription/location
    defaults) is now serialized on save — previously it was silently dropped.
  - New **"Azure (defaults)"** node to edit `azure.subscription` / `azure.location`.
  - The **Resources** tree groups **Entra** and **Azure** types under their
    platform (Fabric types stay at the top level), keeping 100+ types navigable.
  - **Add Resource** dialog gained a **platform filter** (Fabric / Entra / Azure)
    and labels each type by platform + field name + provider type.

## [1.10.0] - 2026-06-15

### Added

- **Fabric Materialized Lake Views** (`materialized_lake_views`) — a new Fabric
  item type (46 Fabric types total): incrementally-refreshed SQL views over
  lakehouse Delta tables, bound to a parent lakehouse, with an optional
  `refresh_cron`.
- **`realtime-governance` template** — a cross-platform real-time intelligence
  estate (IoT Hub → Fabric Eventhouse → Eventstream → Real-Time Activator, plus
  a Lakehouse, Data Agent, and Materialized Lake Views) governed by Azure Key
  Vault, Policy, Defender for Cloud, Monitor, and Sentinel — and **ships an Azure
  DevOps pipeline** (`azure-pipelines.yml`) with validate → staging → prod
  approval-gated stages. Scaffold with
  `udpi30 init --template realtime-governance`.
- New example [`examples/13-realtime-governance`](examples/13-realtime-governance);
  `materialized_lake_views` added to the `all-resource-types` catalogue template.

### Removed

- **README §9 (comparison with fabric-automation-bundles)** — the standalone
  comparison section was removed; the acknowledgment (now §10) is retained.

## [1.9.0] - 2026-06-15

### Added

- **38 more Azure service types** (64 Azure types total), each declared under its
  own `azure_*` key and deployed as a single ARM resource via generated Bicep:
  - **Governance / security / AI:** `azure_purview_accounts`, `azure_key_vaults`,
    `azure_policy_assignments`, `azure_defender_plans`,
    `azure_machine_learning_workspaces`, `azure_ai_foundry`, `azure_sentinel`,
    `azure_video_indexer`, `azure_openai`, `azure_load_testing`,
    `azure_agent_ids`, `azure_service_groups`.
  - **Monitoring / observability:** `azure_monitor_components`, `azure_alerts`,
    `azure_diagnostic_settings`, `azure_log_analytics_workspaces`,
    `azure_metrics`, `azure_workbooks`, `azure_activity_logs`,
    `azure_database_watchers`.
  - **Networking:** `azure_network_watchers`, `azure_dns_zones`,
    `azure_network_interfaces`, `azure_private_dns_zones`,
    `azure_public_ip_addresses`, `azure_route_tables`, `azure_virtual_networks`,
    `azure_local_network_gateways`, `azure_peering_services`, `azure_peerings`,
    `azure_virtual_network_gateways`, `azure_virtual_wans`,
    `azure_ddos_protection_plans`, `azure_firewalls`, `azure_ip_groups`,
    `azure_network_security_groups`, `azure_application_gateways`,
    `azure_application_security_groups`.
- New example [`examples/12-azure-platform-services`](examples/12-azure-platform-services)
  cataloguing all 38; JSON schema (both copies) gains the 38 keys; the
  ARM-type → API-version table in `AzureResourceProvider` is extended to cover them.

### Changed

- **README fully rewritten** — multi-platform overview, a dedicated platform
  providers section, a complete Azure service catalogue, an acknowledgment of and
  a side-by-side comparison with the upstream
  [fabric-automation-bundles](https://github.com/dereknguyenio/fabric-automation-bundles)
  by Derek Nguyen, of which udpi30 is a .NET port and multi-platform extension.

## [1.8.0] - 2026-06-14

### Added

- **23 first-class Azure service resource types**, each declared in `udp.yml`
  under its own `azure_*` key and deployed as a single ARM resource via a
  generated, resource-group-scope Bicep template:
  - **Integration / streaming / compute:** `azure_data_factories`,
    `azure_databricks_workspaces`, `azure_databricks_structured_streaming`,
    `azure_event_hub_namespaces`, `azure_event_grid_topics`,
    `azure_stream_analytics_jobs`, `azure_iot_hubs`, `azure_logic_apps`,
    `azure_functions`.
  - **Storage:** `azure_blob_storage`, `azure_data_lake_storage`, `azure_files`,
    `azure_queue_storage`, `azure_table_storage` (deploy as storage accounts;
    3–24 lowercase alphanumeric names).
  - **Databases & data movement:** `azure_sql_databases`,
    `azure_sql_managed_instances`, `azure_sql_virtual_machines`,
    `azure_postgresql`, `azure_mysql`, `azure_mariadb`,
    `azure_cosmosdb_accounts`, `azure_redis_cache`, `azure_data_box`.
- Generic `AzureServiceResource` model (`resource_group`, `location`, `sku`,
  `kind`, `properties`, `tags`) and a generic Bicep emitter in
  `AzureResourceProvider` driven by an ARM-type → API-version table, with a
  JSON-to-Bicep literal serializer for the `properties` bag.
- New example [`examples/11-azure-data-services`](examples/11-azure-data-services)
  cataloguing every service type; JSON schema (both copies) gains a shared
  `resourceMap_azure_service` definition and the 23 keys.

### Changed

- `AzureResourceProvider` now dispatches by **field name** (the three bespoke
  types keep dedicated paths; everything else flows through the generic emitter),
  avoiding ARM-type collisions among the storage-family services.

## [1.7.0] - 2026-06-14

### Added

- **Multi-platform resources.** A single `udp.yml` can now declare resources
  across three control planes, selected by a per-type `Platform` tag on the
  resource registry:
  - **Microsoft Entra** (Microsoft Graph, tenant scope): `entra_groups` and
    `entra_apps` (app registrations with optional service principal). Create is
    idempotent — an existing object with the same display name is patched.
  - **Azure** (ARM via Bicep through the `az` CLI): `azure_resource_groups`,
    `azure_storage_accounts`, and a generic `azure_deployments` escape hatch that
    deploys any author-supplied `.bicep` file at subscription or resource-group
    scope. A top-level `azure:` block provides subscription/location defaults.
- `IResourcePlatformProvider` abstraction with `EntraResourceProvider` and
  `AzureResourceProvider`; the deploy loop dispatches non-Fabric items to their
  provider while the Fabric path is unchanged.
- Platform-aware name validation (Fabric character rules no longer apply to
  Entra/Azure names; storage account names are checked for the 3–24 lowercase
  alphanumeric rule).
- New example [`examples/10-azure-and-entra`](examples/10-azure-and-entra) and a
  [Multi-platform guide](docs/guide/multi-platform.md).

### Changed

- `ResourceTypeInfo.FabricType` renamed to `ProviderType` (the platform-native
  type identifier); `FabricType` retained as a compatibility alias. The forward
  `ItemTypeMap` (field → provider type) now spans all platforms; the reverse map
  used by `generate` remains Fabric-scoped.

### Notes

- The deploy orchestration is still workspace-centric: a deployment containing
  only Entra/Azure resources will still create a Fabric workspace. Mixed and
  Fabric-only deployments are unaffected.
