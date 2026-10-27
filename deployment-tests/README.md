# Deployment example test harness

This directory contains a standalone PowerShell 7 harness for the 14 deployment
examples. It treats every example as a black-box unit: it reads files under
`examples/`, stages an isolated copy, invokes the documented `udpi30` CLI,
and verifies observable Fabric, Azure, Entra, and Purview outcomes. It does not
load or reference the repository's .NET assemblies.

## Run modes

| Mode | Behavior | Cloud mutation |
| --- | --- | --- |
| `Preflight` | Discovers inputs, assets, principals, tools, and blockers | No |
| `Validate` | Runs strict CLI validation against staged manifests | No |
| `Plan` | Runs validation and the live deployment/admin plan | No |
| `Deploy` | Runs validation, plan, deployment, verification, and cleanup | Yes |

Run locally:

```powershell
pwsh ./deployment-tests/Invoke-DeploymentTestSuite.ps1 -Mode Preflight -Verbose
pwsh ./deployment-tests/Invoke-DeploymentTestSuite.ps1 -Mode Validate -Verbose
pwsh ./deployment-tests/Invoke-DeploymentTestSuite.ps1 -Mode Deploy `
  -InputsPath ./deployment-tests/config/inputs.local.json -Verbose
```

Use `-ExampleId 01,04,10` to select examples, `-KeepResources` to retain
deployment resources, and `-WhatIf` to exercise orchestration without invoking
commands. `-KeepResources` never suppresses tenant-setting restoration.

## Example policy

All examples are present in the shared inventory and therefore in both CI
pipelines. Their default execution depth reflects what the repository actually
ships, rather than silently treating a catalogue as a deployable workload.

| IDs | Default policy | Reason |
| --- | --- | --- |
| 01, 04 | Deployable after capacity input | Complete local assets |
| 02, 03, 05-07, 10, 13, 14 | Deployable with inputs | External assets, principals, endpoints, or Azure values are required |
| 08 | Validation only | Its README identifies it as a copy/paste catalogue with missing definitions and list-only types |
| 09 | Admin plan only | Changes are tenant-wide; mutation requires an explicit restoration manifest |
| 11, 12 | Contract only | They catalogue generic Azure types whose service-required ARM properties are intentionally incomplete |

Set `exampleOverrides.<id>.allowMutation` to `true` only after supplying every
missing input and asset. Example 09 additionally requires
`-AllowTenantAdminChanges` and `exampleOverrides.09.adminRestoreManifest`.

## Inputs

Copy [inputs.example.json](config/inputs.example.json) to the ignored
`config/inputs.local.json`, then replace only the values needed by the selected
examples. The file must not contain credentials. Secrets are supplied by named
environment-variable bindings.

Manual CI runs expose an optional `inputs_path` (GitHub Actions) or `inputsPath`
(Azure DevOps) parameter. It must point to a repository-relative, reviewed JSON
file; absolute and escaping paths are rejected. Use it for non-secret asset
overlays, example overrides, and example 09's restoration manifest. Keep secret
values in the protected environment or secret-variable store.

The runner also recognizes these environment variables directly:

| Variable | Purpose |
| --- | --- |
| `UDP_TEST_FABRIC_CAPACITY_ID` | Fabric capacity assigned to test workspaces |
| `UDP_TEST_AZURE_SUBSCRIPTION_ID` | Azure subscription for isolated test resource groups |
| `UDP_TEST_ENTRA_TENANT_ID` | Entra tenant ID and Key Vault tenant value |
| `UDP_TEST_ADLS_CONNECTION_ID` | Existing Fabric connection for shortcut examples |
| `UDP_TEST_DATA_ENG_GROUP_ID` | Existing Data Engineers group object ID for example 09 |
| `UDP_TEST_CONTRACTORS_GROUP_ID` | Existing Contractors group object ID for example 09 |
| `UDP_TEST_BI_ADMINS_GROUP_ID` | Existing BI Admins group object ID for example 09 |
| `UDP_TEST_PRINCIPAL_MAPPINGS_JSON` | JSON object mapping example group names to existing object IDs |
| `UDP_TEST_TEXT_REPLACEMENTS_JSON` | JSON object mapping illustrative endpoints to real test endpoints |
| `UDP_TEST_SLACK_WEBHOOK` | Example 05 Slack notification secret |
| `PROD_DB_HOST` | Example 05 production database host secret |

Secrets remain `${secret.NAME}` references in source, staged manifests, and
published evidence. The harness checks only whether each named environment
variable is present; it never copies the value into `values`, YAML, JSON, HTML,
or command arguments. Ordinary input keys containing names such as `secret`,
`token`, `password`, `api_key`, `webhook`, or `connection_string` are rejected.

Input overlays are containment checked. Sources must remain under the directory
containing the input manifest, destinations must remain under the staged example,
symbolic links are rejected, and overlays cannot replace `udp.yml`. Text
replacements are limited to exact illustrative external bindings discovered in
the source manifest. Principal mappings must resolve to Entra object IDs.

Every run emits `preflight-inputs.json`. Each missing value states why it is
needed, whether it can be generated, and how to supply it.

## Authentication and authorization

The GitHub workflow uses `azure/login` with OIDC and grants only
`contents: read` to the validation job. Only the Plan/Deploy job receives
`id-token: write`. Configure the protected `deployment-tests` GitHub environment
with `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
`UDP_TEST_FABRIC_CAPACITY_ID`, `UDP_TEST_SLACK_WEBHOOK`, and `PROD_DB_HOST`.
Restrict the federated identity credential to this repository and the
`deployment-tests` environment. Configure that environment's deployment branch
rule to allow only `main`; the workflow also rejects Plan/Deploy unless
`github.ref` is `refs/heads/main`. See [Azure Login with OIDC](https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect).

The Azure DevOps pipeline expects `UDP_AZURE_SERVICE_CONNECTION` to name an
Azure Resource Manager service connection that uses workload identity
federation. Use the Microsoft Entra issuer for new connections, leave **Grant
access permission to all pipelines** disabled, and authorize only this pipeline.
Add a Branch Control check for `refs/heads/main` to the protected service
connection or deployment environment. The YAML also fails before authentication
and conditions the Azure CLI task when `Build.SourceBranch` is not `main`.
See [Azure Resource Manager service connections](https://learn.microsoft.com/azure/devops/pipelines/library/connect-to-azure?view=azure-devops).
Configure `UDP_TEST_SLACK_WEBHOOK` and `PROD_DB_HOST` as secret variables.

The Preflight/Validate jobs deliberately receive no protected environment,
service connection, variable group, or deployment secret. They use only a
syntactically valid dummy capacity GUID with `--skip-connection-check`. GitHub
secrets are available only to the `main`-gated `deployment-tests` environment.
For Azure DevOps, place deployment values and secrets in the protected variable
group `udp-deployment-tests` and add its own Branch Control check for
`refs/heads/main`; a service-connection check alone does not protect variables.

Both pipelines publish `dotnet/src/Udpi30.Cli/Udpi30.Cli.csproj` from the
checked-out commit and invoke the resulting `udpi30.dll`. They do not install
`latest` or a separately published package, so the examples validate the exact
source revision that triggered the manual run.

Access must be scoped to the dedicated test resources. Do not grant Owner or a
subscription-wide Contributor role as a shortcut. The harness follows the
[PSAutoRBAC](https://github.com/PatrickGallucci/PSAutoRBAC) provider model:

1. Discover the command requirement with the platform's authoritative source.
2. Evaluate the caller's current access at the target scope.
3. Generate idempotent grant and revoke guidance.
4. Apply only after operator approval.
5. Revoke temporary access after the run.

The harness never grants permissions. On an authorization failure it records
the platform, ARM action and scope when Azure supplies them, and recommends a
PSAutoRBAC probe. Generic Graph 403 errors are not parsed into guessed roles.

Typical minimums vary by example:

- Fabric Contributor for item-only examples; Fabric Admin when workspace
  security is declared.
- Resource-specific Azure roles at the test resource-group scope. Resource
  group creation needs a narrowly scoped bootstrap permission.
- Graph read permissions for existing principal resolution; group/application
  write permissions only for example 10 creation.
- Fabric administrator plus `Tenant.ReadWrite.All` only for an explicitly
  approved example 09 mutation.
- A Purview data-plane identity for the account readiness probe. The harness uses
  the `https://purview.azure.net` audience and performs only Get Account
  Properties; grant the narrowest provider-supported read role.

## CI pipelines

- GitHub Actions: `.github/workflows/deployment-examples.yml`
- Azure DevOps: `cicd/deployment-examples.azure-devops.yml`

Both pipelines are manual-only, run the same Pester tests and suite entry point,
use sequential execution to reduce Fabric/ARM throttling, and publish evidence
even when the harness returns a failure. Preflight and Validate can run from any
branch; only Plan and Deploy are restricted to the protected `main` branch.

Before a Deploy mutation, the harness proves that the isolated Fabric workspace,
every declared Azure resource group, and every declared Entra display name are
absent. A collision blocks the example. Successful deployment must also emit
creation evidence for the Fabric workspace and Entra objects.

The harness then writes `ownership-receipt.json` containing provider-native IDs,
the run marker, Fabric item IDs, and every resource ID found in each declared
Azure resource group. Before an Azure group enters the receipt, the harness
matches its exact ID in the current subscription deployment operations and
requires `Create`, `Succeeded`, and the resource-provider status `201` or
`Created`; `200` or `OK` proves an update and blocks ownership. It writes the
marker to the Fabric workspace description, the Azure resource-group tag
`udpi30TestRun`, and the Entra group description or application notes.
Immediately before cleanup it re-reads the exact IDs and markers and compares the
complete child-resource sets. Any ID, marker, or child drift refuses deletion.
Cleanup uses the recorded Fabric workspace ID, ARM resource-group ID, and Graph
object IDs; it never rediscovers a deletion target by display name. This favors
leak-on-uncertainty over deleting a foreign object.

Azure resource-group creation is a create-or-update ARM operation and the
2021-04-01 resource-group contract does not expose a lifecycle ETag or creation
timestamp. The harness therefore combines an immediate absence check,
high-entropy names, the deployment operation's `201 Created` result, a run tag,
exact child-ID capture, and pre-delete revalidation. This closes the destructive
cleanup ambiguity but cannot make the create-or-update request itself atomic; a
foreign actor could still win the narrow interval before ARM handles the PUT.
Run this pipeline only with a dedicated test subscription or tightly scoped test
resource-group prefix.

Tenant-admin example 09 additionally requires the restoration manifest to name
exactly the same `admin.tenant_settings` keys and writable fields as the staged
mutation manifest. Its restore plan must be a no-op before mutation. The harness
then reads every page of the live tenant settings API and captures the complete
pre-mutation values for each field the example can change, including empty group
and property arrays. It records only a SHA-256 fingerprint in evidence; response
values remain in memory and private temporary files are deleted immediately.
Failure to delete private response or request-body files fails the run. Private
API responses are never copied into published permission evidence.

Cleanup posts the captured values directly to the Fabric tenant-setting update
API, paced below the 25 requests/minute limit, and polls the full live snapshot
for exact convergence. Unknown settings, missing pages, partial snapshots,
restore errors, mismatched fields, or the 15-minute propagation deadline all fail
closed. The API is preview; example 09 remains plan-only by default and mutation
requires explicit operator approval plus the static restore manifest.

Azure resources may mutate only a resource group declared in the same staged
manifest. Generic subscription-scope Bicep is validation/plan-only in this
harness because the CLI has no bounded automated cleanup path for arbitrary
subscription resources.

For `azure_purview_accounts`, ARM existence is not sufficient. After deployment,
the harness also calls the documented read-only Purview Account Data Plane
endpoint and requires the exact account name with provisioning state
`Succeeded`. A private-only account therefore requires a runner with DNS and
network access to its account private endpoint; a hosted runner will fail closed.

## Evidence and exit codes

Each run writes under `deployment-test-artifacts/<run-id>/`:

- `deployment-test-report.html`: self-contained human report
- `summary.json`: aggregate machine-readable result
- `preflight-inputs.json`: missing input/asset contract
- `permission-findings.json`: least-privilege failure evidence
- `events.jsonl`: structured event stream
- `examples/<id-name>/`: command stdout, stderr, staged manifest, result JSON,
  and (after a successful deployment) `ownership-receipt.json`

Exit codes:

| Code | Meaning |
| --- | --- |
| `0` | Requested work passed, or `-WhatIf` completed |
| `1` | A command, verification, cleanup, or harness operation failed |
| `2` | Preflight inputs or safety prerequisites block the requested mutation |

## Well-Architected trade-offs

- **Security:** federation and least privilege reduce credential exposure, but
  require identity bootstrap outside the harness.
- **Reliability:** preflight, timeouts, isolated names, and cleanup reduce blast
  radius, at the cost of longer runs.
- **Performance efficiency:** sequential deployment avoids throttling; selected
  IDs provide a faster feedback loop.
- **Cost optimization:** catalogue examples do not create expensive resources
  by default, and deploy runs clean up unless `-KeepResources` is set.
- **Operational excellence:** GitHub Actions, Azure DevOps, and local execution
  share one versioned runner and one evidence format.
