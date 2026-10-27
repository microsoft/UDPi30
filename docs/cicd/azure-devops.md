# Run the deployment examples in Azure DevOps

The repository includes a manual Azure DevOps pipeline that exercises the
deployment harness against all 14 folders under `examples/`:

- Pipeline: `cicd/deployment-examples.azure-devops.yml`
- Harness reference: `deployment-tests/README.md`

The pipeline has no continuous integration or pull request trigger. An operator
must start every run and choose its execution depth. This design keeps cloud
mutation separate from ordinary validation.

## Automated project bootstrap

Use `cicd/Initialize-AzureDevOpsDeploymentExamples.ps1` to discover the project
and Azure subscription metadata, configure the protected resources, register
the existing YAML pipeline, queue a run, wait for completion, and download the
evidence artifact. Start with `-WhatIf`:

```powershell
pwsh ./cicd/Initialize-AzureDevOpsDeploymentExamples.ps1 `
   -Organization contoso `
   -Project 'Data Platform' `
   -GitHubRepository microsoft/UDPi30 `
   -GitHubServiceConnectionName PatrickGallucci `
   -Mode Preflight `
   -WhatIf
```

The script creates or reuses these resources idempotently:

- A Microsoft Entra app registration and service principal without a password,
   unless `-ServicePrincipalAppId` identifies an existing identity.
- An Azure Resource Manager service connection using workload identity
   federation and the Microsoft Entra issuer.
- The `udp-deployment-tests` variable group.
- The `udp-deployment-examples` YAML pipeline connected to the GitHub repository.
- Pipeline-specific permissions for the GitHub connection, Azure connection,
   and variable group. It never enables open access for every pipeline.
- Branch control on the Azure connection and variable group, requiring a
   protected `refs/heads/main` branch and rejecting unknown protection status.

The script requires a ready GitHub service connection backed by the Azure
Pipelines GitHub App or an existing OAuth connection. GitHub consent is an
interactive authorization boundary, so the script discovers and reuses that
connection instead of creating or accepting a long-lived GitHub PAT. If more
than one connection is available, pass its exact name with
`-GitHubServiceConnectionName`.

Secret values are read only from `UDP_TEST_SLACK_WEBHOOK` and `PROD_DB_HOST` in
the process environment. Existing secrets are preserved by default; pass
`-UpdateSecrets` to replace them from the current environment. Non-secret
optional values can use their documented `UDP_TEST_*` environment variables or
the `-Variable` hashtable. Values whose names look sensitive are rejected from
`-Variable`.

For `Plan` and `Deploy`, supply the Fabric capacity through
`-FabricCapacityId` or `UDP_TEST_FABRIC_CAPACITY_ID`. The script does not guess
or automatically grant an Azure role. To manage a direct assignment, pass both
`-AzureRoleDefinitionName` and an explicit Azure resource ID in
`-AzureRoleScope`. A subscription-scoped `Contributor` assignment is suitable
only for a dedicated test subscription whose purpose and blast radius have
been reviewed.

```powershell
$env:UDP_TEST_FABRIC_CAPACITY_ID = '<capacity-guid>'
$env:UDP_TEST_SLACK_WEBHOOK = '<secret-value>'
$env:PROD_DB_HOST = '<secret-value>'

pwsh ./cicd/Initialize-AzureDevOpsDeploymentExamples.ps1 `
   -Organization contoso `
   -Project 'Data Platform' `
   -GitHubRepository microsoft/UDPi30 `
   -GitHubServiceConnectionName PatrickGallucci `
   -AzureSubscriptionId '<dedicated-test-subscription-guid>' `
   -Mode Deploy
```

The default output is written below the ignored
`deployment-test-artifacts/bootstrap-<timestamp>/` directory. The bootstrap
summary contains resource IDs and run status but not secret values. When the
pipeline completes, the script downloads
`deployment-example-report-<run-id>` and extracts failed or blocked examples
from the harness `summary.json`.

> **Important:** A blank example selection includes all 14 examples, but it does
> not make every catalogue entry deployable. The harness applies each example's
> checked-in safety policy. Examples 08, 11, and 12 remain validation or contract
> checks, and example 09 remains an admin plan unless its additional safeguards
> are explicitly satisfied.

## What the pipeline does

Every run performs these common steps:

1. Checks out the selected commit without persisting repository credentials.
2. Installs .NET 9 from `global.json`.
3. Installs Pester 5.7.1 and runs the harness test suite.
4. Builds `udpi30.dll` from the checked-out source.
5. Runs the requested harness mode.
6. Attempts to publish the evidence directory, including on a failed harness run.

The four modes form a promotion path:

| Mode | Result | Azure sign-in | Cloud mutation |
| --- | --- | --- | --- |
| `Preflight` | Discovers required tools, files, values, principals, and blockers | No | No |
| `Validate` | Runs strict validation against isolated staged manifests | No | No |
| `Plan` | Runs validation and live deployment or administration planning | Yes | No |
| `Deploy` | Plans, deploys, verifies, and cleans up eligible examples | Yes | Yes |

`Preflight` and `Validate` do not load the protected variable group or Azure
service connection. `Plan` and `Deploy` load both and are accepted only from
`refs/heads/main`.

## Prerequisites

Before configuring the pipeline, obtain:

- An Azure DevOps project with the repository available to Azure Pipelines.
- Permission to create pipelines, service connections, variable groups, and
  approvals and checks.
- A protected `main` branch.
- A dedicated test scope in Azure and a Fabric capacity for test workspaces.
- The non-secret IDs and optional secrets required by the examples you intend to
  run.

Use a dedicated test subscription when the selected examples must create their
own resource groups. Otherwise, scope the deployment identity to dedicated test
resource groups. Do not grant `Owner` or subscription-wide `Contributor` as a
shortcut.

## Step 1: Prepare non-secret inputs

Run `Preflight` before deciding which values are required. For a local starting
point, copy `deployment-tests/config/inputs.example.json` and fill only the
sections needed by the selected examples.

For Azure DevOps, the `inputsPath` parameter must identify a reviewed file in
the repository. The harness rejects absolute paths and paths that escape the
repository. The file can contain asset overlays, example overrides, principal
mappings, and restoration metadata, but it must not contain credentials.

Supply secrets through protected variables. Keys that look like passwords,
tokens, API keys, webhooks, or connection strings are rejected in the JSON
input.

## Step 2: Create the workload identity service connection

Create a secret-free Azure Resource Manager connection:

1. In Azure DevOps, open **Project settings** > **Service connections**.
2. Select **New service connection** > **Azure Resource Manager**.
3. Choose an automatically managed app registration and select **Workload
   identity federation** as its credential.
4. Select the narrowest Azure scope that can support the chosen examples.
5. Enter a recognizable name, such as `udp-deployment-tests-wif`.
6. Leave **Grant access permission to all pipelines** cleared.
7. Save the connection and use **Verify** to confirm it can authenticate.

If automatic creation is unavailable because of tenant permissions, use
Microsoft's manual workload identity procedure. Do not fall back to a client
secret merely to complete setup.

The federated identity still needs Azure RBAC. Assign only the provider actions
required by the selected examples and only at the dedicated test scope. The
harness reports authorization failures but never grants access itself.

## Step 3: Create the protected variable group

Open **Pipelines** > **Library**, create a variable group named
`udp-deployment-tests`, and add the values that apply to your run.

| Variable | Purpose | Mark secret |
| --- | --- | --- |
| `UDP_AZURE_SERVICE_CONNECTION` | Exact name of the workload identity service connection | No |
| `UDP_TEST_FABRIC_CAPACITY_ID` | Capacity assigned to isolated Fabric test workspaces | No |
| `UDP_TEST_AZURE_SUBSCRIPTION_ID` | Subscription used by Azure examples | No |
| `UDP_TEST_ENTRA_TENANT_ID` | Entra tenant and Key Vault tenant value | No |
| `UDP_TEST_ADLS_CONNECTION_ID` | Existing Fabric connection used by shortcut examples | No |
| `UDP_TEST_DATA_ENG_GROUP_ID` | Existing Data Engineers group object ID | No |
| `UDP_TEST_CONTRACTORS_GROUP_ID` | Existing Contractors group object ID | No |
| `UDP_TEST_BI_ADMINS_GROUP_ID` | Existing BI Admins group object ID | No |
| `UDP_TEST_PRINCIPAL_MAPPINGS_JSON` | JSON map from example names to existing object IDs | No |
| `UDP_TEST_TEXT_REPLACEMENTS_JSON` | JSON map from illustrative bindings to test bindings | No, unless organizational policy treats an endpoint as sensitive |
| `UDP_TEST_SLACK_WEBHOOK` | Slack notification endpoint used by example 05 | Yes |
| `PROD_DB_HOST` | Database host binding used by example 05 | Yes |

You do not need to invent placeholder values. A `Preflight` run produces
`preflight-inputs.json`, which identifies each missing value, why it is needed,
and how to supply it.

## Step 4: Protect both deployment resources

The YAML branch guard is one layer, not the entire control. Add checks and
pipeline permissions to both resources consumed by `Plan` and `Deploy`:

1. Open the service connection's **Approvals and checks** page.
2. Add a **Branch control** check that allows only `refs/heads/main`.
3. Require the branch to be protected and fail the check when its protection
   state cannot be determined.
4. Repeat the same Branch control check on the `udp-deployment-tests` variable
   group.
5. Optionally add a manual approval to the service connection or variable group
   for authenticated runs.

Checks are owned by the protected resource and are not stored in the YAML, so a
pipeline edit cannot silently remove them. An environment check has no effect on
this pipeline unless the YAML is changed to consume that environment.

## Step 5: Register the YAML pipeline

1. Open **Pipelines** > **New pipeline**.
2. Select the repository source and repository.
3. Choose **Existing Azure Pipelines YAML file**.
4. Select the `main` branch.
5. Set the path to `cicd/deployment-examples.azure-devops.yml`.
6. Save the pipeline without starting a deployment.
7. Give the pipeline a clear name, such as `udp-deployment-examples`.

Now authorize only this pipeline:

1. On the service connection, open **Pipeline permissions** and add the new
   pipeline.
2. On the `udp-deployment-tests` variable group, open **Pipeline permissions**
   and add the same pipeline.
3. Confirm that neither resource is open to all pipelines.

If Azure DevOps shows a resource authorization prompt on the first authenticated
run, a resource administrator can grant access to this pipeline only.

## Step 6: Run all 14 examples in Preflight

1. Open **Pipelines** and select `udp-deployment-examples`.
2. Select **Run pipeline**.
3. Choose the branch containing the commit to inspect.
4. Set the parameters as follows:

   | Parameter | First-run value |
   | --- | --- |
   | `mode` | `Preflight` |
   | `exampleIds` | Leave blank to include all 14 examples |
   | `inputsPath` | Leave blank, or enter a reviewed repository-relative JSON path |
   | `keepResources` | `false` |
   | `allowTenantAdminChanges` | `false` |

5. Select **Run**.
6. When the run completes, download the evidence artifact and review
   `preflight-inputs.json` before configuring additional values.

Preflight uses a syntactically valid dummy Fabric capacity ID and does not use
the deployment identity. It is therefore appropriate for feature branches.

## Step 7: Promote the same revision safely

Use the modes in order. Resolve findings before moving to the next depth.

### Validate

Queue `Validate` with `exampleIds` blank to run strict validation across all 14
examples. This stage remains unauthenticated and non-mutating.

### Plan

Queue `Plan` from `main` after validation passes. The run consumes the protected
variable group and service connection, exercises live planning, and does not
mutate cloud resources.

Start with a small selection when testing new permissions:

```text
01,04
```

The `exampleIds` value accepts comma-separated IDs from `01` through `14`.
Leave it blank when you want the harness to process the full inventory under
each example's safe default policy.

### Deploy

Queue `Deploy` from `main` only after reviewing the plan and permission
findings. Keep `keepResources` set to `false` for routine runs so eligible test
resources are removed after verification.

Set `keepResources` to `true` only when an operator needs the isolated resources
for investigation. Tenant-setting restoration is still attempted even when
deployment resources are retained.

## Parameter reference

| Parameter | Accepted value | Notes |
| --- | --- | --- |
| `mode` | `Preflight`, `Validate`, `Plan`, or `Deploy` | Controls the maximum execution depth |
| `exampleIds` | Blank or comma-separated IDs | Blank processes all 14 examples; for example, `01,04,10` selects three |
| `inputsPath` | Blank or repository-relative JSON path | Non-secret input only; traversal and absolute paths are rejected |
| `keepResources` | `true` or `false` | Applies to deployment resources, not tenant-setting restoration |
| `allowTenantAdminChanges` | `true` or `false` | Relevant only to the guarded example 09 mutation path |

## Example 09: tenant administration safeguard

Example 09 changes tenant-wide settings and is plan-only by default. Do not turn
on `allowTenantAdminChanges` as a general pipeline option.

A mutation requires all of the following:

- `exampleOverrides.09.allowMutation` set to `true` in the reviewed input file.
- A complete `exampleOverrides.09.adminRestoreManifest` in that file.
- `allowTenantAdminChanges` set to `true` for the individual run.
- The required Fabric administrator and Microsoft Graph permissions.
- Operator review of the no-op restore plan before mutation.

The harness captures the prior settings, restores them after the test, and
fails closed if it cannot prove exact convergence. The tenant settings API is a
preview API, so use this path only in a controlled tenant with explicit approval.

## Download and review evidence

The final task publishes a pipeline artifact named:

```text
deployment-example-report-<Build.BuildId>
```

To retrieve it, open the completed run and select the published artifact from
the run summary. The artifact contains `harness-tests.xml` and a run-specific
evidence directory. Review these files first:

| File | Use |
| --- | --- |
| `deployment-test-report.html` | Human-readable result and example status |
| `summary.json` | Aggregate machine-readable outcome |
| `preflight-inputs.json` | Missing values, assets, tools, and safety prerequisites |
| `permission-findings.json` | Least-privilege authorization evidence |
| `events.jsonl` | Ordered structured event stream |
| `examples/<id-name>/` | Per-example stdout, stderr, staged manifest, result, and ownership evidence |

The pipeline uses `condition: always()` for publication, so it attempts to
preserve diagnostic evidence after a harness failure.

## Exit codes

| Code | Meaning | Next action |
| --- | --- | --- |
| `0` | Requested work passed | Review evidence before promoting to the next mode |
| `1` | A command, verification, cleanup, or harness operation failed | Inspect the report, events, and the affected example's stderr |
| `2` | Inputs or safety prerequisites block the requested depth | Resolve `preflight-inputs.json`, then rerun the same mode |

## Troubleshooting

### The pipeline is not authorized to use a resource

Confirm that the pipeline appears in **Pipeline permissions** for both the
service connection and `udp-deployment-tests`. Do not solve this by enabling
access for every pipeline.

### Branch control rejects Plan or Deploy

Confirm all of the following:

- The queued source branch is exactly `refs/heads/main`.
- `main` has an Azure Repos branch policy or equivalent protection.
- Both protected resources allow the full branch reference.
- The check is configured to fail rather than accept an unknown protection
  state.

### Azure authentication fails

Use **Verify** on the Azure Resource Manager service connection. Confirm it uses
workload identity federation and that the Azure CLI task can obtain a token.
`AzureCLI@2`, which this pipeline uses, supports workload identity federation.

### The harness reports an authorization failure

Open `permission-findings.json`. Grant only the missing provider action at the
reported test scope, rerun `Plan`, and remove temporary access after the test.
Do not infer a broad role from a generic Microsoft Graph `403` response.

### A private Purview account fails verification

The harness verifies Purview through its read-only data-plane endpoint, not ARM
existence alone. A Microsoft-hosted agent cannot reach a private-only account
unless it has the required DNS and network route. Move the job to a reviewed
self-hosted agent pool with private endpoint connectivity rather than weakening
the Purview network boundary.

### Cleanup refuses to delete a resource

This is an intentional safety outcome. The harness deletes only resources whose
provider IDs, run markers, and child-resource sets still match its ownership
receipt. Investigate the drift and remove retained test resources manually only
after confirming ownership.

## Security and operational trade-offs

| Well-Architected pillar | Pipeline choice | Trade-off |
| --- | --- | --- |
| Security | Workload identity, pipeline-scoped permissions, and branch checks | More initial identity and RBAC setup |
| Reliability | Preflight, verification, sequential execution, and fail-closed cleanup | Longer end-to-end runs |
| Performance efficiency | `exampleIds` supports focused feedback | Full inventory runs remain intentionally sequential to reduce throttling |
| Cost optimization | Cleanup is the default and catalogue-only entries do not deploy | Retaining resources for diagnosis can incur cost |
| Operational excellence | The pipeline builds the exact commit and publishes one evidence format | Operators must review artifacts before promotion |

## Microsoft references

- [Connect to Azure with an Azure Resource Manager service connection](https://learn.microsoft.com/azure/devops/pipelines/library/connect-to-azure?view=azure-devops#create-an-app-registration-with-workload-identity-federation-automatic)
- [Manage variable groups](https://learn.microsoft.com/azure/devops/pipelines/library/variable-groups?view=azure-devops)
- [Define approvals and checks](https://learn.microsoft.com/azure/devops/pipelines/process/approvals?view=azure-devops)
- [Secure your Azure Pipelines](https://learn.microsoft.com/azure/devops/pipelines/security/overview?view=azure-devops)
- [Publish and download pipeline artifacts](https://learn.microsoft.com/azure/devops/pipelines/artifacts/pipeline-artifacts?view=azure-devops)
