# GitHub Actions

This page provides complete, copy-ready workflow files for deploying Unified Data Platform Deployment with GitHub Actions. These workflows have been proven end-to-end against live Fabric workspaces.

For a working reference repository, see [github.com/microsoft/udpi30-example](https://github.com/microsoft/udpi30-example).

---

## Repository example harness

The udpi30 repository includes a manual-only workflow at
`.github/workflows/deployment-examples.yml`. It builds the selected commit and
drives the shared PowerShell harness across all 14 folders under `examples/`.
Use it to validate the shipped catalogue, not as an automatic production
deployment workflow for a consuming project.

| Input | Purpose |
| --- | --- |
| `mode` | Selects `Preflight`, `Validate`, `Plan`, or `Deploy` |
| `example_ids` | Optionally limits the run to comma-separated IDs such as `01,04` |
| `inputs_path` | Points to reviewed, repository-relative non-secret input JSON |
| `keep_resources` | Retains isolated deployment resources for investigation |
| `allow_tenant_admin_changes` | Unlocks the separately guarded example 09 mutation path |

`Preflight` and `Validate` receive no protected environment or Azure token.
`Plan` and `Deploy` require `refs/heads/main`, the protected
`deployment-tests` environment, and OIDC authentication. Pester is installed
and run, and the CLI is restored and built, in a separate prerequisite job with
no OIDC permission or deployment secrets. Plan and deployment jobs consume the
prebuilt CLI artifact before Azure authentication. The workflow publishes the
Pester result separately from the run evidence, including when a job fails.

The implementation and safety contract are documented in
`deployment-tests/README.md`. For the matching Azure DevOps setup, see
[Run the deployment examples in Azure DevOps](azure-devops.md).

---

## 1. Setting up secrets

Go to your repository on GitHub: **Settings > Secrets and variables > Actions**. Add the following repository secrets:

The generic application workflows below use a client secret for broad runner
compatibility. Prefer the OIDC pattern used by the repository example harness
when your GitHub organization and Azure tenant support federation.

| Secret | Description |
|--------|-------------|
| `AZURE_TENANT_ID` | Your Entra ID (Azure AD) tenant GUID |
| `AZURE_CLIENT_ID` | The service principal's application (client) ID |
| `AZURE_CLIENT_SECRET` | The service principal's client secret |

These secrets are referenced as `${{ secrets.AZURE_TENANT_ID }}`, etc., in the workflow files below.

See [Service Principal Setup](../guide/service-principal.md) for instructions on creating the service principal and granting it workspace access.

---

## 2. Setting up environments with approval gates

Go to **Settings > Environments** in your repository. Create the following environments:

| Environment | Configuration |
|-------------|---------------|
| `dev` | No protection rules. Deployments run automatically on merge. |
| `staging` | Optional: add required reviewers if you want a gate before staging. |
| `production` | Required reviewers. Add one or more team members who must approve before production deploys run. Optionally add a wait timer (e.g., 5 minutes) to allow time to cancel. |

You can also scope environment secrets. If dev and prod use different service principals, add the `AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET` secrets at the environment level instead of the repository level.

---

## 3. CI workflow: PR validation

This workflow runs on every pull request that touches the deployment definition. It validates the schema and policies, then runs a plan to show what would change.

Create `.github/workflows/udp-ci.yml`:

```yaml
name: Fabric CI

on:
  pull_request:
    paths:
      - 'udp.yml'
      - 'notebooks/**'
      - 'sql/**'
      - 'agent/**'
      - 'pipelines/**'

jobs:
  validate:
    name: Validate Deployment
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install udpi30
        run: dotnet tool install --global udpi30

      - name: Validate
        run: udpi30 validate --strict

  plan:
    name: Plan (${{ matrix.target }})
    runs-on: ubuntu-latest
    needs: validate
    strategy:
      matrix:
        target: [dev]
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install udpi30
        run: dotnet tool install --global udpi30

      - name: Plan deployment
        run: udpi30 plan -t ${{ matrix.target }}
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}
```

---

## 4. CD workflow: deploy on merge

This workflow runs when changes are merged to `main`. It deploys sequentially through dev, staging, and production, with an approval gate before production.

Create `.github/workflows/udp-cd.yml`:

```yaml
name: Fabric CD

on:
  push:
    branches: [main]
    paths:
      - 'udp.yml'
      - 'notebooks/**'
      - 'sql/**'
      - 'agent/**'
      - 'pipelines/**'

jobs:
  # ── Deploy to Dev (automatic) ─────────────────────
  deploy-dev:
    name: Deploy to Dev
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install udpi30
        run: dotnet tool install --global udpi30

      - name: Deploy
        run: udpi30 deploy --target dev -y
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}

      - name: Verify deployment
        run: udpi30 status --target dev
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}

  # ── Deploy to Staging (automatic after dev) ────────
  deploy-staging:
    name: Deploy to Staging
    runs-on: ubuntu-latest
    needs: deploy-dev
    environment: staging
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install udpi30
        run: dotnet tool install --global udpi30

      - name: Deploy
        run: udpi30 deploy --target staging -y
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}

      - name: Verify deployment
        run: udpi30 status --target staging
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}

  # ── Deploy to Production (requires approval) ──────
  deploy-prod:
    name: Deploy to Production
    runs-on: ubuntu-latest
    needs: deploy-staging
    environment: production
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install udpi30
        run: dotnet tool install --global udpi30

      - name: Deploy
        run: udpi30 deploy --target prod -y
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}

      - name: Verify deployment
        run: udpi30 status --target prod
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}
```

---

## 5. Drift check workflow (scheduled)

This workflow runs on a schedule to detect changes made outside of the deployment pipeline. It reports drift as a workflow annotation and uploads the report as an artifact.

Create `.github/workflows/udp-drift.yml`:

```yaml
name: Drift Check

on:
  schedule:
    - cron: '0 8 * * 1-5'  # Weekdays at 8:00 AM UTC
  workflow_dispatch:

jobs:
  drift:
    name: Check Drift (${{ matrix.target }})
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
        run: udpi30 drift -t ${{ matrix.target }} --format json > drift-report.json
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

      - name: Annotate on drift
        if: steps.drift.outcome == 'failure'
        run: echo "::warning::Drift detected in '${{ matrix.target }}' environment. See drift-report artifact for details."

      - name: Fail on drift
        if: steps.drift.outcome == 'failure'
        run: exit 1
```

---

## 6. Destroy workflow (manual dispatch)

This workflow tears down all deployment resources in a target workspace. It requires manual dispatch and confirmation to prevent accidental deletion.

Create `.github/workflows/udp-destroy.yml`:

```yaml
name: Destroy Environment

on:
  workflow_dispatch:
    inputs:
      target:
        description: 'Target environment to destroy'
        required: true
        type: choice
        options:
          - dev
          - staging
      confirm:
        description: 'Type the target name to confirm destruction'
        required: true
        type: string

jobs:
  destroy:
    name: Destroy ${{ github.event.inputs.target }}
    runs-on: ubuntu-latest
    environment: ${{ github.event.inputs.target }}
    steps:
      - name: Verify confirmation
        run: |
          if [ "${{ github.event.inputs.confirm }}" != "${{ github.event.inputs.target }}" ]; then
            echo "::error::Confirmation does not match target. Expected '${{ github.event.inputs.target }}', got '${{ github.event.inputs.confirm }}'."
            exit 1
          fi

      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install udpi30
        run: dotnet tool install --global udpi30

      - name: Destroy resources
        run: udpi30 destroy -t ${{ github.event.inputs.target }} -y
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}
```

!!! warning "Production protection"
    The `prod` option is intentionally excluded from the destroy workflow's target choices. If you need to destroy production resources, do so through a separate process with additional safeguards.

---

## 7. Example of a working pipeline run

A successful CD run looks like this in the GitHub Actions UI:

```
Fabric CD                                    ✓ completed in 4m 32s
├── Deploy to Dev                            ✓ 1m 12s
│   ├── Checkout                             ✓ 2s
│   ├── Setup .NET 9                         ✓ 8s
│   ├── Install udpi30                   ✓ 15s
│   ├── Deploy                               ✓ 42s
│   │   Deploying to target: dev
│   │   Workspace: contoso-analytics-dev
│   │     ✓ Lakehouse: bronze (unchanged)
│   │     ✓ Lakehouse: silver (unchanged)
│   │     ✓ Notebook: ingest_to_bronze (updated)
│   │     ✓ Pipeline: daily_ingest (unchanged)
│   │   Deployed 4 resources (1 updated, 3 unchanged)
│   └── Verify deployment                    ✓ 5s
├── Deploy to Staging                        ✓ 1m 08s
│   └── ...
└── Deploy to Production                     ⏳ waiting for approval
    └── PatrickGallucci approved               ✓ 2m 12s
```

---

## 8. Reference repository

A complete working example with all four workflows, a multi-target `udp.yml`, and sample notebooks is available at:

[github.com/microsoft/udpi30-example](https://github.com/microsoft/udpi30-example)
