# CI/CD Overview

This page explains why continuous integration and deployment apply to Microsoft Fabric, describes the standard udpi30 pipeline and its authentication model, and compares the supported CI/CD platforms. Platform-specific pipeline files are provided in [GitHub Actions](github-actions.md) and [Azure DevOps](azure-devops.md).

---

## 1. Why CI/CD for Microsoft Fabric

Managing Fabric resources manually through the portal works for a single developer building a prototype. It breaks down the moment a second person touches the workspace, or the moment the same project must be deployed to staging and production. CI/CD for Fabric provides four properties:

| Property | Description |
|---|---|
| Consistency | Every environment is deployed from the same deployment definition. No forgotten manual steps, no "it works on my workspace" problems. |
| Repeatability | Deployments are deterministic. Running the same pipeline twice with the same inputs produces the same workspace state. |
| Auditability | Every change goes through source control. Pull requests create a review trail. Deployment logs record who deployed what, when, and to which target. |
| Safety | Validation catches errors before they reach a workspace. Plan output shows exactly what will change. Approval gates prevent unreviewed changes from reaching production. |

---

## 2. The deployment pipeline

A standard udpi30 pipeline follows this flow:

1. A developer opens a **pull request** with changes to `udp.yml`, notebooks, SQL scripts, or other definitions.
2. The CI pipeline runs **validate** to check schema correctness and policy compliance, then runs **plan** to preview what would change in the target workspace.
3. Reviewers approve and **merge** the pull request.
4. The CD pipeline automatically **deploys to dev**.
5. After dev deployment succeeds, the pipeline **deploys to staging** (optionally with automated tests).
6. A manual **approval gate** is required before **deploying to production**.

```mermaid
flowchart LR
    A[PR Created] --> B[Validate]
    B --> C[Plan]
    C --> D{PR Approved?}
    D -->|Yes| E[Merge to main]
    E --> F[Deploy Dev]
    F --> G[Deploy Staging]
    G --> H{Approval Gate}
    H -->|Approved| I[Deploy Prod]
    H -->|Rejected| J[Pipeline Stopped]
```

### 2.1 Pipeline stages in detail

| Stage | Command | Trigger | Authentication Required | Purpose |
|-------|---------|---------|------------------------|---------|
| Validate | `udpi30 validate --strict` | PR opened/updated | No | Schema and policy checks against local files |
| Plan | `udpi30 plan --target dev` | PR opened/updated | Yes | Dry-run diff against the live workspace |
| Deploy Dev | `udpi30 deploy --target dev -y` | Merge to main | Yes | Deploy to the development workspace |
| Deploy Staging | `udpi30 deploy --target staging -y` | After dev succeeds | Yes | Deploy to the staging workspace |
| Deploy Prod | `udpi30 deploy --target prod -y` | After manual approval | Yes | Deploy to the production workspace |
| Drift Check | `udpi30 drift --target prod` | Scheduled (cron) | Yes | Detect out-of-band changes |

Every pipeline installs the CLI as a .NET global tool before running any command:

```bash
dotnet tool install --global udpi30
```

### 2.2 Repository example harness

Maintainers can exercise all 14 checked-in examples with a separate manual
promotion path:

```text
Preflight -> Validate -> Plan -> Deploy
```

The GitHub workflow is `.github/workflows/deployment-examples.yml`; the Azure
DevOps pipeline is `cicd/deployment-examples.azure-devops.yml`. Both build the
selected source revision instead of installing the latest published package,
publish structured evidence, and reserve cloud mutation for protected `main`.
The individual example policy still limits catalogue-only and tenant-wide
scenarios. See the platform pages for setup and safety controls.

---

## 3. Authentication in CI/CD

udpi30 authenticates to the Fabric API through its `FabricAuth` component, built on the Azure.Identity library. Credential selection follows this order:

| Condition | Credential used |
|-----------|-----------------|
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, and `AZURE_CLIENT_SECRET` are all set | `ClientSecretCredential` (service principal) |
| `FABRIC_USE_BROWSER=true` | `InteractiveBrowserCredential` (interactive browser sign-in) |
| Otherwise | `DefaultAzureCredential` (managed identity, Azure CLI session, and other standard sources) |

No interactive browser login is needed in CI/CD. Prefer workload identity
federation or a managed identity so the pipeline does not store a reusable
credential.

### 3.1 Workload identity federation (recommended)

GitHub Actions can request an OpenID Connect token with `id-token: write` and
sign in through `azure/login`. Azure DevOps can use an Azure Resource Manager
service connection configured for workload identity federation. In both cases,
restrict the federated credential to the repository or pipeline, protect the
deployment environment or variable group, and scope Azure RBAC to the dedicated
deployment resources.

The manual example pipelines use this pattern. Their unauthenticated
`Preflight` and `Validate` modes cannot access deployment credentials.

### 3.2 Service principal (client secret fallback)

For a platform that cannot federate, set three protected environment variables
in the CI/CD runner:

| Variable | Description |
|----------|-------------|
| `AZURE_TENANT_ID` | Your Entra ID (Azure AD) tenant GUID |
| `AZURE_CLIENT_ID` | The app registration's application (client) ID |
| `AZURE_CLIENT_SECRET` | A client secret for the app registration |

When all three variables are set, udpi30 authenticates with `ClientSecretCredential`.

See [Service Principal Setup](../guide/service-principal.md) for step-by-step instructions on creating and configuring the service principal.

### 3.3 Managed identity

For Azure-hosted CI/CD runners (Azure DevOps Microsoft-hosted agents with managed identity, self-hosted runners on Azure VMs, or GitHub Actions runners on Azure), you can use managed identity instead of a client secret. No environment variables are required. When the service principal variables are not set, udpi30 falls back to `DefaultAzureCredential`, which discovers the identity automatically.

This is the most secure option because there is no secret to rotate or leak.

---

## 4. Environment protection

Both GitHub Actions and Azure DevOps support approval gates that prevent deployments to sensitive environments without human review.

### 4.1 GitHub Actions

Create environments in your repository settings (Settings > Environments) and add required reviewers:

| Environment | Protection Rules |
|-------------|-----------------|
| `dev` | None (auto-deploy on merge) |
| `staging` | None, or require specific reviewers |
| `production` | Required reviewers, wait timer (optional) |

Reference the environment in your workflow with `environment: production`. GitHub will pause the job and request approval before it runs.

### 4.2 Azure DevOps

Create environments in Pipelines > Environments and add approval checks:

| Environment | Approvals |
|-------------|-----------|
| `dev` | None |
| `staging` | None |
| `production` | Required approvers |

Reference the environment in your pipeline with `environment: 'production'`. Azure DevOps will pause the stage and request approval.

---

## 5. GitHub Actions vs Azure DevOps

| Capability | GitHub Actions | Azure DevOps |
|-----------|---------------|--------------|
| Workflow definition | `.github/workflows/*.yml` | `azure-pipelines.yml` |
| Secrets management | Repository or environment secrets | Variable groups (with Key Vault linking) |
| Approval gates | Environment protection rules | Environment approvals and checks |
| Scheduled runs | `on: schedule` with cron syntax | `schedules:` trigger |
| Manual trigger | `on: workflow_dispatch` | Manual trigger on pipeline |
| Matrix strategy | `strategy: matrix` | `strategy: matrix` |
| Artifact sharing | `actions/upload-artifact` | Pipeline artifacts |
| Self-hosted runners | Self-hosted runners | Self-hosted agents |

Both platforms are supported. The platform pages provide ordinary project
pipeline patterns and the repository-maintainer harness. Choose the platform
your team already operates, then apply its protected environments, branch
controls, and least-privilege identity model.

---

## 6. Next steps

- [GitHub Actions](github-actions.md) -- Project workflow patterns plus the manual 14-example harness.
- [Azure DevOps](azure-devops.md) -- Secure manual harness setup with workload identity federation and protected resources.
- [Service Principal Setup](../guide/service-principal.md) -- Create and configure the service principal for CI/CD.
- [Secrets Management](../guide/secrets.md) -- Handle connection strings and credentials in pipelines.
