# Drift Detection

This page describes how `udpi30 drift` detects differences between the recorded deployment state and the live Fabric workspace, and how to resolve them. Drift detection is a Stable feature.

Drift occurs when the live state of a Fabric workspace diverges from what is defined in your `udp.yml`. This can happen when someone edits a resource through the Fabric portal, when an external tool modifies an item via the REST API, or when a teammate deploys a change that is not reflected in the deployment definition. Left unchecked, drift erodes the reliability of infrastructure-as-code: what you see in source control no longer matches what is running in production.

---

## 1. How drift detection works

Every time `udpi30 deploy` runs, the StateManager writes a per-target state file (`.udpi30/state-<target>.json`) that records every deployed resource along with a SHA-256 hash of its definition. When you run `udpi30 drift`, the tool:

1. **Reads the state file** to get the list of resources udpi30 previously deployed, including their item IDs and definition hashes.
2. **Queries the Fabric workspace** via the REST API to retrieve the current list of items and their definitions.
3. **Compares the two** and classifies each difference into one of three categories.

No changes are made to the workspace or the state file during a drift check. It is a read-only operation.

---

## 2. Types of drift

| Category | Meaning | Typical cause |
|----------|---------|---------------|
| Added | Items exist in the workspace but are not tracked in the deployment state. | Created outside of udpi30, for example by a user in the Fabric portal or by another deployment tool. |
| Removed | Items are recorded in the deployment state but no longer exist in the workspace. | Someone deleted the item manually, or another process removed it. |
| Modified | Items exist in both the state and the workspace, but the definition hash in the state file no longer matches the hash of the live item's definition. | Someone edited the resource in the portal, for example modifying a notebook's code or changing a pipeline's activities. |

---

## 3. Using `udpi30 drift`

### 3.1 Basic usage

```bash
# Check drift against the default target
udpi30 drift

# Check drift against a specific target
udpi30 drift --target prod
```

### 3.2 Example output

```
$ udpi30 drift --target dev

Drift Report for target: dev
Workspace: contoso-analytics-dev (c2410443-5bce-4cce-8065-b453dd6b2f1d)
Compared against state from: 2026-03-25T14:32:10Z

  Added (2):
    + Notebook: ad_hoc_analysis (not tracked by deployment)
    + Report: executive_dashboard (not tracked by deployment)

  Removed (1):
    - Lakehouse: staging_lakehouse (expected but missing from workspace)

  Modified (1):
    ~ Notebook: ingest_to_bronze (definition changed)
        Local hash:  a3f8c2d1e5b7...
        Remote hash: 7b2e4f9a1c3d...

Summary: 4 resources drifted (2 added, 1 removed, 1 modified)
```

### 3.3 Machine-readable output

Use `--format json` to produce output suitable for scripts and CI/CD:

```bash
udpi30 drift --target dev --format json
```

```json
{
  "target": "dev",
  "workspace_id": "c2410443-5bce-4cce-8065-b453dd6b2f1d",
  "state_timestamp": "2026-03-25T14:32:10Z",
  "added": [
    {"name": "ad_hoc_analysis", "type": "Notebook"},
    {"name": "executive_dashboard", "type": "Report"}
  ],
  "removed": [
    {"name": "staging_lakehouse", "type": "Lakehouse"}
  ],
  "modified": [
    {"name": "ingest_to_bronze", "type": "Notebook"}
  ],
  "drift_count": 4
}
```

### 3.4 Exit codes

| Exit Code | Meaning |
|-----------|---------|
| `0` | Check completed, with or without drift found |
| `1` | Error (deployment failed to load, workspace unreachable) |

Note that `drift` currently exits `0` even when drift is detected. To fail a CI/CD job on drift, parse the command output (for example, grep for `Drift detected`) rather than relying on the exit code.

---

## 4. Automated drift checks in CI/CD

Scheduled drift checks catch configuration drift before it causes problems. A common pattern is to run a drift check on a cron schedule and notify the team when drift is found.

### 4.1 GitHub Actions example

```yaml
name: Drift Check

on:
  schedule:
    - cron: '0 8 * * 1-5'  # Weekdays at 8:00 AM UTC
  workflow_dispatch:         # Allow manual trigger

jobs:
  drift:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        target: [dev, staging, prod]
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install udpi30
        run: dotnet tool install --global udpi30

      - name: Check for drift
        id: drift
        run: |
          udpi30 drift -t ${{ matrix.target }} --format json > drift-report.json
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}
        continue-on-error: true

      - name: Upload drift report
        if: steps.drift.outcome == 'failure'
        uses: actions/upload-artifact@v4
        with:
          name: drift-report-${{ matrix.target }}
          path: drift-report.json

      - name: Notify on drift
        if: steps.drift.outcome == 'failure'
        run: |
          echo "::warning::Drift detected in ${{ matrix.target }} environment. Review drift-report artifact."
```

---

## 5. Resolving drift

Once drift is detected, you have two options depending on which version of the truth you want to keep.

### 5.1 Option 1: Overwrite the workspace (deployment wins)

If the deployment definition is correct and the workspace should match it, redeploy:

```bash
udpi30 deploy --target dev -y
```

This pushes the deployment definitions to the workspace, overwriting any manual changes. Added items that are not in the deployment are left untouched (udpi30 only manages resources it tracks).

### 5.2 Option 2: Update the deployment to match the workspace

If the changes made in the workspace are intentional and should be preserved, update your `udp.yml` to reflect the new state:

```bash
# Export the current workspace definitions to local files
udpi30 export --target dev

# Review the exported changes, update udp.yml as needed, then redeploy
udpi30 deploy --target dev -y
```

For items that were added outside the deployment and should now be managed, use `udpi30 bind`:

```bash
udpi30 bind --item-id <item-guid> --resource-key new_notebook --target dev
```

For items that were deleted from the workspace and should also be removed from the deployment, delete the resource entry from `udp.yml` and redeploy.

---

## 6. Integration with the state file

Drift detection depends entirely on the state file. If no state file exists for a target (because you have never deployed to it), `udpi30 drift` will return an error:

```
Error: No state file found for target 'prod'.
  Run 'udpi30 deploy --target prod' or 'udpi30 import --target prod' first.
```

When using a remote state backend (OneLake, Azure Blob, or ADLS Gen2), the drift command reads the remote state file so that any CI runner can check for drift without needing a local copy. See [State Management](state.md) for backend configuration and stability status.

The drift command never writes to the state file. Only `deploy`, `import`, `bind`, and `rollback` modify state.
