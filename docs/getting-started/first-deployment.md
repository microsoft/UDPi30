# Your First Deployment

This tutorial deploys a minimal hand-written `udp.yml` containing one lakehouse and one notebook. It walks through the file structure, the source files, and what the engine does during a deploy.

---

## 1. Prerequisites

| Requirement | Details |
|---|---|
| udpi30 installed | `dotnet tool install --global udpi30` (see [Installation](installation.md)) |
| Authentication configured | `az login`, or service principal environment variables |
| Fabric capacity | An active capacity (F2 or higher) and its GUID |
| Workspace permissions | Admin or Contributor role, or permission to create workspaces |

---

## 2. Define the deployment

Create a minimal `udp.yml`:

```yaml
deployment:
  name: hello-udp
  version: "1.0.0"

workspace:
  capacity_id: "your-capacity-guid"

resources:
  lakehouses:
    my_lakehouse:
      description: "My first lakehouse"

  notebooks:
    hello_notebook:
      path: ./notebooks/hello.py
      description: "Hello world notebook"

targets:
  dev:
    default: true
    workspace:
      name: hello-udp-dev
```

The top-level sections map to the configuration model in `Udpi30.Core.Models`:

| Section | Model class | Purpose |
|---|---|---|
| `deployment` | `DeploymentDefinition` | Name and version. Available in variables as `${deployment.name}` and `${deployment.version}`. |
| `workspace` | `WorkspaceConfig` | Capacity assignment and workspace defaults. |
| `resources` | `ResourcesConfig` | The Fabric items to deploy, grouped by type. |
| `targets` | `TargetConfig` | Per-environment overrides such as the workspace name. |

---

## 3. Create the notebook source

Fabric notebooks run Python, so the notebook source is a `.py` file:

```bash
mkdir notebooks
echo '# Hello from Unified Data Platform Deployment
print("It works!")' > notebooks/hello.py
```

---

## 4. Validate and deploy

```bash
udpi30 validate
udpi30 deploy --target dev
```

`validate` checks the file locally without calling the Fabric API. `deploy` applies the desired state to the `dev` target.

---

## 5. What happens during a deploy

The engine processes the definition in five stages:

| Stage | Component | Function |
|---|---|---|
| 1. Load | Loader / YamlFactory | Parses `udp.yml` with YamlDotNet, processes includes, substitutes variables, and validates against the schema. |
| 2. Resolve | Resolver | Sorts resources topologically by dependency so items deploy in the correct order. |
| 3. Plan | Planner | Diffs desired state against actual workspace state and classifies each resource as Create, Update, Delete, or No-op. |
| 4. Deploy | Deployer | Executes the planned actions through the Fabric REST API (`FabricClient`) and updates state as each action completes. |
| 5. Record | StateManager | Persists `deployment-state.json`, which enables drift detection and idempotent re-runs. |

Re-running `udpi30 deploy --target dev` with no changes results in a no-op plan: the run is idempotent.

---

## 6. Next steps

- [Quick start](quickstart.md) -- Generate a full medallion project from a template.
- [udp.yml reference](../guide/udp-yml.md) -- Complete schema reference for deployment definitions.
- [Environment variables](../reference/environment-variables.md) -- Authentication and variable reference.
