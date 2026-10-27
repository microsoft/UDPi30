# Unified Data Platform Deployment

> **Public Preview**

Unified Data Platform Deployment (`udpi30`) is a declarative deployment tool for Microsoft Fabric, Microsoft Entra, and Azure resources, distributed primarily as .NET global tools. This page describes the deployment model, the gap the tool fills relative to existing tooling, and its principal capabilities.

---

## 1. Overview

Define an entire Fabric project in a single `udp.yml` file: lakehouses, notebooks, pipelines, semantic models, Data Agents, security roles, and environment targets. Validate, plan, and deploy with a single command.

The `udp.yml` file describes the **desired state** of the workspace. `udpi30` determines what to create, update, or delete to reach that state, applies the changes in dependency order, runs idempotently, and detects drift on every run. The model will be familiar to users of Terraform or Databricks Asset Deployments. See [the declarative model](guide/declarative-model.md) for details.

```bash
dotnet tool install --global udpi30
udpi30 init --template medallion --name udp-project
udpi30 deploy --target dev
```

Installation requires the .NET SDK 9.0 or later. See [Installation](getting-started/installation.md) for prerequisites and authentication setup.

Download the [product and operations brief](assets/udpi30-product-brief.docx)
or the [v1.14.0 release overview](assets/udpi30-release-overview.pptx) for
offline sharing.

---

## 2. Scope and positioning

Existing tools cover adjacent needs. The Fabric CLI can export and import items, `fabric-cicd` can deploy across workspaces, and Terraform or Bicep can provision infrastructure. None of them describe the project itself:

| Concern | Description |
|---|---|
| Resource inventory | Which resources the project requires |
| Dependencies | How those resources depend on each other |
| Environment configuration | How configuration varies across environments (dev, staging, prod) |
| Security | Which security roles and permissions are required |
| Ordering | How to deploy everything in the correct order |

Unified Data Platform Deployment fills that gap.

---

## 3. Features

| Feature | Description |
|---|---|
| 46 Fabric item types | Lakehouses, Notebooks, Pipelines, Warehouses, Semantic Models, Reports, Environments, Data Agents, KQL Databases, Eventhouses, Materialized Lake Views, dbt Jobs, and 34 more |
| Multi-platform | Entra groups/apps (Microsoft Graph) and Azure resources (Bicep via `az`) in the same `udp.yml` — see the [Multi-platform guide](guide/multi-platform.md) |
| Dependency resolution | Automatic topological sort for deployment ordering |
| Multi-environment | Dev, staging, and prod targets with variable overrides |
| State management | Tracks deployed resources and detects drift |
| Rollback support | Deployment history with point-in-time rollback |
| Security | Entra ID group, user, and service principal role assignments with Graph API resolution |
| Secrets | Environment variables and Azure Key Vault integration |
| CI/CD ready | GitHub Actions and Azure DevOps templates included |
| Deployment assurance | A black-box harness exercises all 14 repository examples through local, GitHub Actions, or Azure DevOps runs |
| Ontology Builder | A Windows application imports a Fabric semantic model, generates editable ontology metadata, and publishes a Fabric Ontology item |
| Policy enforcement | Configurable pre-deploy validation rules |
| Tenant settings | Declaratively manage org-wide Fabric admin/tenant settings via a gated `admin apply` |

Use the [Ontology Builder](guide/ontology-builder.md) for semantic-model-driven
ontology authoring. Maintainers can use the [CI/CD guides](cicd/overview.md) to
validate and safely exercise the complete example catalogue.
