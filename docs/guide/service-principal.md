# Service Principal Setup

This page describes how to create and configure the service principal (SP) required for CI/CD deployments. The service principal authenticates udpi30 to the Fabric API without user interaction.

When the environment variables `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, and `AZURE_CLIENT_SECRET` are set, udpi30 authenticates with a `ClientSecretCredential`. If they are not set, `DefaultAzureCredential` applies (managed identity, environment, or `az login`). Setting `FABRIC_USE_BROWSER=true` forces an `InteractiveBrowserCredential` for local interactive sign-in.

---

## 0. Automated setup (recommended)

`scripts\Get-UdpYmlValues.ps1 -ProvisionServicePrincipal` creates the service principal **only if it does not exist** (default name `sp-udpi30`) and grants everything udpi30 needs. It is idempotent — existing apps, groups, and grants are detected and skipped — and honors `-WhatIf`.

```powershell
# Preview: nothing is created or changed
.\scripts\Get-UdpYmlValues.ps1 -ProvisionServicePrincipal -WhatIf -NonInteractive

# Create + grant, with a client secret stored in Key Vault and secret access to that vault
.\scripts\Get-UdpYmlValues.ps1 -ProvisionServicePrincipal -CreateClientSecret `
    -SecretKeyVaultName kv-governance -KeyVaultNames kv-governance -NonInteractive
```

### Update an existing udp.yml

Discovery normally writes reports and a YAML snippet without changing your deployment file.
To merge discovered values directly into an existing file:

```powershell
# Required only for updating an existing file
Install-Module powershell-yaml -Scope CurrentUser

# Preview the merge, then apply it
.\scripts\Get-UdpYmlValues.ps1 -UdpFile .\examples\01-minimal\udp.yml -Targets dev -NonInteractive -WhatIf
.\scripts\Get-UdpYmlValues.ps1 -UdpFile .\examples\01-minimal\udp.yml -Targets dev -NonInteractive
```

`-UdpFile` updates the fields emitted by the generated snippet, preserving resources,
unrelated settings, the existing deployment version, and any existing default target.
Variable defaults are updated in either scalar or mapping form; security roles merge by
name, retaining unrelated roles and additional role settings. Choose `-Targets` to match
the environments you intend to update. Unresolved optional values are not cleared.
The file is not used as input to Azure/Fabric discovery; discovery still uses the
script parameters and its normal defaults.

The update honors `-WhatIf` / `-Confirm` and saves the original bytes to a uniquely named
`<udp-file>.<id>.bak` backup. YAML serialization can change formatting and remove comments;
review the diff before deployment. Invalid YAML or incompatible mapping sections cause an
error rather than overwriting the file. A file changed during discovery is also rejected.
If the path does not exist, the script warns and does not create it.
The normal reports are still generated, including during `-WhatIf`; the JSON report's
`udpFile` field records update status and backup location.

| Grant (`-GrantScopes`) | What it assigns                                                                                                                                                                                              |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Graph`                | Application permissions with admin consent:`Group.ReadWrite.All`, `GroupMember.ReadWrite.All`, `Application.ReadWrite.All`, `User.Read.All`, `Directory.Read.All` (Entra types, principal lookups) |
| `Azure`                | `-AzureRoles` on the subscription — default `Contributor`, `Resource Policy Contributor`, `Security Admin`. Add `User Access Administrator` for resource locks or role assignments in Bicep       |
| `FabricWorkspace`      | `-WorkspaceRole` (default `admin`) on every target workspace that already exists                                                                                                                         |
| `FabricTenant`         | Membership in`-FabricAccessGroupName` (default `sg-fabric-automation`, created if missing)                                                                                                               |
| `StateStorage`         | `Storage Blob Data Contributor` on the `azureblob`/`adls` state account                                                                                                                                |
| `KeyVault`             | `Key Vault Secrets User` (or a get/list access policy) on each `-KeyVaultNames` vault                                                                                                                    |

Steps that cannot be automated are reported as `MANUAL`, never skipped silently:

- Enable the Fabric tenant settings `ServicePrincipalAccessPermissionAPIs` and `ServicePrincipalAccessGlobalAPIs` (and the admin-API settings for `admin plan/apply`) for the `-FabricAccessGroupName` group. [`examples/00-default`](https://github.com/microsoft/UDPi30/tree/main/examples/00-default) declares them; apply with `udpi30 admin apply` as a Fabric administrator.
- Add the service principal as a Contributor of the Fabric capacity.
- Assign a Purview collection role if the identity touches Purview data-plane objects.

The operator running the script needs rights to create apps and grant admin consent (Global Administrator, or Application Administrator plus Privileged Role Administrator), Owner or User Access Administrator on the subscription, and Admin on the workspaces. The client secret is never printed or written to the report; without `-SecretKeyVaultName` it is set as `AZURE_CLIENT_SECRET` in the current process only.

The numbered steps below are the equivalent manual procedure.

---

## 1. Create the App Registration

```bash
az ad app create --display-name "sp-udpi30"
```

Note the `appId` from the output. This is your `AZURE_CLIENT_ID`.

---

## 2. Create a Client Secret

```bash
az ad app credential reset --id <appId> --append
```

Copy the `password`. This is your `AZURE_CLIENT_SECRET`. You cannot retrieve it later.

---

## 3. Create the Service Principal

```bash
az ad sp create --id <appId>
```

---

## 4. Get Your Tenant ID

```bash
az account show --query tenantId -o tsv
```

This is your `AZURE_TENANT_ID`.

---

## 5. Grant Fabric Workspace Access

The service principal needs the **Contributor** or **Admin** role on each workspace it deploys to.

### 5.1 Option A: Grant via Fabric Portal

1. Open the workspace in app.fabric.microsoft.com
2. Settings > Manage access
3. Add the service principal by name or app ID
4. Set role to **Contributor** (or **Admin** for security role management)

### 5.2 Option B: Grant via udpi30

Add the SP to your udp.yml security roles:

```yaml
security:
  roles:
    - name: cicd_deployer
      service_principal: "<appId>"
      workspace_role: admin
```

Then deploy once with your personal account to grant the SP access.

### 5.3 Option C: Grant via API

```bash
# Get the SP object ID
SP_OBJECT_ID=$(az ad sp show --id <appId> --query id -o tsv)

# Grant workspace access (requires existing workspace)
az rest --method post \
  --url "https://api.fabric.microsoft.com/v1/workspaces/<workspaceId>/roleAssignments" \
  --resource "https://api.fabric.microsoft.com" \
  --body "{\"principal\": {\"id\": \"$SP_OBJECT_ID\", \"type\": \"ServicePrincipal\"}, \"role\": \"Admin\"}"
```

---

## 6. Configure CI/CD

### 6.1 GitHub Actions

Go to repo Settings > Secrets and variables > Actions:

| Secret                  | Value         |
| ----------------------- | ------------- |
| `AZURE_TENANT_ID`     | Tenant GUID   |
| `AZURE_CLIENT_ID`     | App ID        |
| `AZURE_CLIENT_SECRET` | Client secret |

### 6.2 Azure DevOps

Go to Pipelines > Library and create a variable group:

| Variable                | Value         | Secret? |
| ----------------------- | ------------- | ------- |
| `AZURE_TENANT_ID`     | Tenant GUID   | No      |
| `AZURE_CLIENT_ID`     | App ID        | No      |
| `AZURE_CLIENT_SECRET` | Client secret | Yes     |

---

## 7. Test Locally

```bash
export AZURE_TENANT_ID="..."
export AZURE_CLIENT_ID="..."
export AZURE_CLIENT_SECRET="..."
udpi30 deploy --target dev
```

---

## 8. Minimum Permissions

The service principal needs:

| Permission                         | Why                                         |
| ---------------------------------- | ------------------------------------------- |
| Fabric workspace Contributor/Admin | Create, update, delete items                |
| Fabric API access                  | Authenticate to`api.fabric.microsoft.com` |
| Microsoft Graph (optional)         | Resolve Entra group display names to GUIDs  |

---

## 9. Secret Rotation

Client secrets expire. Set a reminder to rotate before expiry:

```bash
# Create new secret
az ad app credential reset --id <appId> --append --end-date "2027-01-01"

# Update CI/CD secrets with the new value
# Delete the old secret from Azure Portal
```

---

## 10. Managed Identity (Alternative)

For Azure-hosted runners, you can use managed identity instead of client secrets:

1. Enable managed identity on your Azure VM / App Service / AKS
2. Grant the managed identity workspace access (same as SP)
3. `DefaultAzureCredential` automatically uses the managed identity; no environment variables are needed

This is the most secure option for production CI/CD.
