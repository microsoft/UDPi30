# State Management

This page describes how udpi30 records what it has deployed, where the state file lives, and how to configure remote state backends for team and CI/CD use. udpi30 tracks deployments in a state file, similar to Terraform's `terraform.tfstate` or the internal state of Databricks Asset Bundles; the state enables incremental deploys, drift detection, and rollback.

---

## 1. How it works

Every time you run `udpi30 deploy`, the StateManager updates the state file with:

| Recorded data | Description |
|---|---|
| Deployed resources | Name, item ID, and type of every resource the deployment manages |
| Definition hashes | SHA-256 of each resource's definition, used for incremental deploy |
| Workspace ID | Which workspace was deployed to |
| Timestamp | When the deployment happened |

On the next deploy, udpi30 compares local definitions against stored hashes. **Unchanged resources are skipped.** Only modified resources are re-uploaded.

---

## 2. State file location

By default, state is stored locally in one file per target, `state-<target>.json`, inside the `.udpi30/` state directory:

```
udp-project/
├── udp.yml
├── .udpi30/                      # State directory
│   ├── state-dev.json              # Deployment state (one per target)
│   ├── lock-dev.lock               # Deployment lock (temporary)
│   ├── history/                    # Deployment history
│   │   ├── 1774451090-dev.json
│   │   └── 1774451200-dev.json
│   ├── audit.jsonl                 # Structured audit log
│   └── metrics.json                # Deployment metrics
└── .gitignore                      # Should include .udpi30/
```

!!! warning "Don't commit state files"
    Add `.udpi30/` to your `.gitignore`. State is environment-specific: each developer and CI runner has their own.

---

## 3. State file format

```json
{
  "version": 1,
  "deployment_name": "contoso-analytics",
  "deployment_version": "1.0.0",
  "target_name": "dev",
  "workspace_id": "c2410443-5bce-4cce-8065-b453dd6b2f1d",
  "workspace_name": "contoso-analytics-dev",
  "last_deployed": 1774451090.617839,
  "resources": {
    "bronze": {
      "item_id": "f207dd97-d7ae-4fed-88c0-5f4a38ff934b",
      "item_type": "Lakehouse",
      "resource_key": "bronze",
      "definition_hash": "a3f8c2d1e5b7...",
      "last_deployed": 1774451090.617839
    },
    "ingest_to_bronze": {
      "item_id": "f1714aa7-636b-48ca-8ef2-b85d5c59583c",
      "item_type": "Notebook",
      "resource_key": "ingest_to_bronze",
      "definition_hash": "b4e9d3f2a6c8...",
      "last_deployed": 1774451090.617839
    }
  }
}
```

---

## 4. State backends

For teams and CI/CD, local state does not work because each runner gets its own copy. Use a remote backend to share state across machines. The Azure SDK ships inside the .NET tool; no additional installation is required for any backend.

| Backend | Status | Locking | Notes |
|---------|--------|---------|-------|
| Local (default) | Stable | File lock | `state-<target>.json` in `.udpi30/` |
| Azure Blob Storage | Beta | Blob lease | Comparable to Terraform's `azurerm` backend |
| OneLake | Beta | Blob lease | State lives alongside your data in a Fabric lakehouse |
| ADLS Gen2 | Beta | Blob lease | Azure Data Lake Storage Gen2 filesystem |

### 4.1 Local (default)

No configuration needed. State is stored in `.udpi30/`.

```yaml
# No state config = local
```

### 4.2 Azure Blob Storage

```yaml
state:
  backend: azureblob
  config:
    account_name: mystorageaccount
    container_name: udpi30-state
    # Recommended: omit account_key so DefaultAzureCredential is used
    # account_key: "${secret.STORAGE_KEY}"
```

Prefer omitting the account key. When no key is provided, udpi30 authenticates with `DefaultAzureCredential`, so the same service principal or managed identity used for Fabric also covers state access.

### 4.3 OneLake

Store state in a Fabric lakehouse so that state lives alongside your data.

```yaml
state:
  backend: onelake
  config:
    workspace_id: "your-workspace-guid"
    lakehouse_id: "your-lakehouse-guid"
    path: ".udpi30-state"   # Optional, default: .udpi30-state
```

State files are stored in `Files/.udpi30-state/` in the lakehouse. Uses the OneLake ADLS-compatible endpoint (`onelake.dfs.fabric.microsoft.com`).

### 4.4 Azure Data Lake Storage Gen2

```yaml
state:
  backend: adls
  config:
    account_name: mydatalake
    filesystem: udpi30-state
```

As with the Blob backend, omit account keys where possible so `DefaultAzureCredential` is used.

---

## 5. Comparison with other tools

| | Terraform | Databricks Asset Bundles (DABs) | udpi30 |
|---|-----------|-------------------|-----------|
| State file | `terraform.tfstate` | `.databricks/bundle/{target}/terraform.tfstate` | `.udpi30/state-{target}.json` |
| Remote backends | S3, Azure Blob, GCS, etc. | Managed by Databricks | **OneLake**, Azure Blob, ADLS Gen2 |
| Locking | DynamoDB / Blob lease | Terraform under the hood | Blob lease / file lock |
| Incremental | Yes (hash comparison) | Yes | Yes (definition hash) |
| Drift detection | `terraform plan` | No | `udpi30 drift` |
| Rollback | Manual state manipulation | No | `udpi30 rollback` |
| History | No (state is overwritten) | No | Yes (timestamped snapshots) |

---

## 6. Deployment locking

Deployment locking is Stable for both local state (file lock) and remote backends (Azure Blob lease). When using a remote backend, udpi30 takes an **Azure Blob lease** to prevent concurrent deployments. If two CI runners try to deploy to the same target simultaneously, the second one will see:

```
Deployment locked by runner@github-actions (CI: 12345678)
  Use --force to override.
```

Locks automatically expire after 30 minutes (stale lock protection).

---

## 7. Commands

```bash
# View current state
udpi30 status --target dev

# View deployment history
udpi30 history --target dev

# Detect drift (compare state vs live workspace)
udpi30 drift --target dev

# Rollback to previous deployment
udpi30 rollback --last --target dev

# Override a stale lock
udpi30 deploy --target dev --force
```

---

## 8. Importing existing resources

If you have resources already deployed (by Terraform, fabric-cicd, or manually), import them into udpi30 state:

```bash
# From a live workspace
udpi30 import --workspace "udp-existing-workspace" --target dev

# From Terraform state
udpi30 import --from-terraform terraform.tfstate --target dev
```

This creates the state file without redeploying. udpi30 will manage the resources going forward.
