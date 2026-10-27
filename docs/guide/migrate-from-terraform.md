# Migrate from Terraform

This page describes how to migrate from the [Terraform Fabric Provider](https://registry.terraform.io/providers/microsoft/fabric/latest/docs) to udpi30: the differences between the two tools, the three adoption flows for bringing existing resources under management, and the step-by-step migration procedure.

---

## 1. Why migrate

| Capability | Terraform | udpi30 |
|---|-----------|-----------|
| Language | HCL | YAML |
| State | Remote (S3, Blob, etc.) | Local JSON (stable); Azure Blob and OneLake/ADLS Gen2 (beta) |
| Learning curve | High (HCL, providers, modules) | Low (single YAML file) |
| Drift detection | `terraform plan` | `udpi30 drift` |
| Rollback | Manual state manipulation | `udpi30 rollback` |
| Fabric-specific | Generic provider | Purpose-built for Fabric (+ Entra & Azure) |
| MCP support | No | Yes (14 tools) |
| Item types | ~15 | 46 Fabric + Entra + 64 Azure |

---

## 2. Adoption flows

udpi30 ships three complementary commands for bringing existing resources under management. Pick the one that matches your starting point:

| Command | Use when | Scope |
|---|---|---|
| [`udpi30 import --from-terraform`](../cli/commands.md#import) | You already manage Fabric with Terraform and want to migrate in bulk. | Reads `terraform.tfstate`, extracts all `fabric_*` resources, and seeds udpi30 state. |
| [`udpi30 generate`](../cli/commands.md#generate) | You have a workspace but no declaration yet and want to reverse-engineer a `udp.yml`. | Scans a live workspace and writes `udp.yml` plus item content (notebook source, etc.). Add `--include-entra` / `--include-azure` to also import deployed Entra and Azure resources, so a Terraform estate spanning Fabric + Entra + Azure reverse-engineers in one pass. |
| [`udpi30 bind`](../cli/commands.md#bind) | You wrote the declaration by hand and want to attach it to an existing item without recreating it. | Per-resource; binds one entry in `udp.yml` to one live item by ID. |

> **Compared to Databricks Asset Bundles**
>
> `databricks bundle generate` exists but is per-resource-type and requires the existing item's ID. `databricks bundle deployment bind` is the direct analog of `udpi30 bind`. DABs have **no equivalent of `udpi30 import --from-terraform`**; migrating from Terraform on Databricks is a manual `generate` + `bind` per resource. Bulk `tfstate` ingestion is unique to udpi30.

---

## 3. Step-by-step migration

### 3.1 Import existing state

```bash
udpi30 import --from-terraform terraform.tfstate --target dev
```

This reads your Terraform state and creates a udpi30 state file.

### 3.2 Generate udp.yml

```bash
udpi30 generate --workspace "your-workspace-name"
```

Or manually map your Terraform resources:

| Terraform | udp.yml |
|-----------|------------|
| `fabric_workspace` | `targets.dev.workspace` |
| `fabric_lakehouse` | `resources.lakehouses` |
| `fabric_notebook` | `resources.notebooks` |
| `fabric_warehouse` | `resources.warehouses` |
| `fabric_environment` | `resources.environments` |
| `fabric_data_pipeline` | `resources.pipelines` |

### 3.3 Validate

```bash
udpi30 validate
udpi30 plan --target dev
```

### 3.4 Deploy

```bash
udpi30 deploy --target dev
```

### 3.5 Remove Terraform

Once udpi30 is managing your resources, remove the Terraform config. Keep the Terraform state file as a backup until you are confident.
