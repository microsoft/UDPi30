# Troubleshooting

This page lists common errors by category, with the cause and resolution for each. For a general environment check, run `udpi30 diag`, which validates the .NET runtime, Azure CLI status, and Fabric API connectivity.

---

## 1. Authentication

### 1.1 "DefaultAzureCredential failed to retrieve a token"

**Cause:** Not authenticated with Azure. When the service principal variables are not set, the tool falls back to `DefaultAzureCredential`, which requires a managed identity, environment credentials, or an active `az login` session.

**Resolution:**

```bash
# Interactive login
az login

# Or set service principal env vars (ClientSecretCredential)
export AZURE_TENANT_ID="your-tenant-guid"
export AZURE_CLIENT_ID="your-client-guid"
export AZURE_CLIENT_SECRET="your-secret"
```

### 1.2 "Invalid client secret provided"

**Cause:** The `AZURE_CLIENT_SECRET` value is wrong. A common mistake is copying the Secret ID instead of the Secret Value.

**Resolution:** In the Azure Portal, go to App registrations, select the app, open Certificates & secrets, create a new secret, and copy the **Value** (not the ID).

### 1.3 "AADSTS7000215: Invalid client secret"

**Cause:** Same as 1.2, or the secret has expired.

**Resolution:** Create a new client secret and update the GitHub secrets or environment variables.

---

## 2. Capacity

### 2.1 "capacity_id is not a valid GUID"

**Cause:** The `capacity_id` in `udp.yml` is not in the correct format.

**Resolution:** Find the capacity GUID:

```bash
az rest --method get \
  --url "https://api.fabric.microsoft.com/v1/capacities" \
  --resource "https://api.fabric.microsoft.com"
```

Copy the `id` field (format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`).

### 2.2 "Capacity not found" or deploy creates workspace but items fail

**Cause:** The capacity is paused or inactive, or the identity does not have access to it.

**Resolution:** In the Fabric Admin Portal, open Capacities and confirm the capacity shows "Active".

---

## 3. Item creation

### 3.1 "DisplayName is Invalid for ArtifactType"

**Cause:** Lakehouses, warehouses, and some other item types cannot have hyphens or spaces in their names.

**Resolution:** Use underscores instead of hyphens:

```yaml
# Wrong
lakehouses:
  bronze-lakehouse:  # ← hyphens not allowed

# Right
lakehouses:
  bronze_lakehouse:  # ← underscores only
```

### 3.2 "ItemDisplayNameNotAvailableYet"

**Cause:** An item with the same name was recently deleted. Fabric reserves the name for a few minutes.

**Resolution:** Wait 2-5 minutes and retry. udpi30 automatically retries retriable errors, but if the wait exceeds the retry window, run deploy again.

### 3.3 "NotebookId cannot be null"

**Cause:** Pipeline activities reference notebooks by ID, but the notebook was not found in the workspace.

**Resolution:** Ensure notebooks are deployed before pipelines. udpi30 handles this automatically through dependency ordering, but if a pipeline references notebooks from a different deployment, deploy those notebooks first.

### 3.4 "Artifact definition parts count should be 1"

**Cause:** Spark Job Definitions only accept a single definition part.

**Resolution:** This was a bug in earlier versions. Update to the latest version: `dotnet tool update --global udpi30`

### 3.5 "InvalidDefinitionFormat"

**Cause:** The notebook definition format is not recognized by the Fabric API.

**Resolution:** Ensure the notebook files are valid `.py` or `.ipynb` files. udpi30 wraps `.py` files in ipynb format automatically.

### 3.6 "MissingDefinition"

**Cause:** Semantic Models and Reports require definition files (TMDL/PBIR) that do not exist locally.

**Resolution:**

| Item type | Action |
| --- | --- |
| Semantic Models | Export TMDL files from Power BI Desktop or the Fabric portal and place them in the `path` directory. |
| Reports | Export PBIR files from Power BI Desktop and place them in the `path` directory. |
| Either | Alternatively, remove the items from `udp.yml` and create them in the portal. |

### 3.7 "The feature is not available"

**Cause:** The item type requires a capacity feature that is not enabled (for example, dbt or EventSchemaSet).

**Resolution:** Contact your Fabric admin to enable the feature, or remove the item from `udp.yml`.

---

## 4. OneLake security

### 4.1 "UniversalSecurityFeatureDisabledForArtifactType"

**Cause:** OneLake data access roles require the security feature to be enabled per lakehouse.

**Resolution:**

1. Open the lakehouse in the Fabric portal.
2. Click **Manage OneLake security (preview)** in the ribbon.
3. Enable the feature.
4. Run `udpi30 deploy` again.

> **Note**
>
> This is a per-item setting, not a tenant admin setting.

---

## 5. Environment

### 5.1 "There is a publish operation in progress"

**Cause:** A previous environment publish is still running. Environment publishes can take 5-10 minutes.

**Resolution:** Wait for the current publish to complete. Check status in the Fabric portal under the environment's details. udpi30 publishes are fire-and-forget, so the deploy itself succeeds.

---

## 6. Variables

### 6.1 "Unresolved variables: ${var.missing}"

**Cause:** A variable referenced in `udp.yml` has no value defined.

**Resolution:** Provide a value through one of the following:

| Approach | Example |
| --- | --- |
| Default value | `variables: { missing: { default: "value" } }` |
| Target override | `targets: { dev: { variables: { missing: "value" } } }` |
| Environment variable | Set the variable when using `${env.MISSING}` |
| Secret | Set the secret before `plan` or `deploy` when using `${secret.MISSING}` |

In deploy mode, unresolved variables cause a hard failure. In validate mode, use `--strict` to catch unresolved `${var.*}` and `${env.*}` references. `${secret.*}` and `${keyvault.*}` references remain deferred until plan or deploy, as described in [Secrets Management](guide/secrets.md).

---

## 7. Deployment

### 7.1 "Deployment locked by user@host"

**Cause:** A previous deployment did not release its lock (for example, it crashed mid-deploy).

**Resolution:**

```bash
udpi30 deploy --target dev --force  # Override the lock
```

Or manually delete the lock file:

```bash
rm .udpi30/lock-dev.lock
```

### 7.2 Partial deployment / rollback

**Cause:** Some items failed during creation. By default udpi30 then **rolls back** every item it created in that run, so the deployment is all-or-nothing.

**Resolution:** Check the error messages for the failed items. Fix the issues (naming, definitions, permissions) and run deploy again — items from previous runs show as "update" instead of "create".

To keep the items that succeeded instead of rolling them all back, deploy with `--continue-on-error`:

```bash
udpi30 deploy --target dev --continue-on-error
```

The deployment finishes as a partial success (exit code `1`), records what was created, and leaves the failed items for you to fix and redeploy. Items whose definition is missing (for example a semantic model or report with no definition files) are **skipped** with a warning rather than failing the deployment.

---

## 8. CI/CD

### 8.1 GitHub Actions: "Process completed with exit code 1"

**Cause:** A pipeline step failed. Common causes:

| Cause | Detail |
| --- | --- |
| Authentication | Secrets not configured or expired |
| Capacity | Not active, or wrong GUID |
| Naming | Hyphens in resource names |

**Resolution:** Check the full log output in the Actions run. Run `udpi30 diag` locally to diagnose.

### 8.2 "udpi30: command not found" in CI

**Cause:** udpi30 is not installed in the CI environment.

**Resolution:** Add `dotnet tool install --global udpi30` before running udpi30 commands.

---

## 9. Admin / tenant settings

### 9.1 `admin plan` reports a setting as "unknown"

**Cause:** The key under `admin.tenant_settings` is not a valid `settingName` for your tenant. The [tenant settings index](https://learn.microsoft.com/en-us/fabric/admin/tenant-settings-index) lists display titles, not the API identifiers udpi30 needs.

**Resolution:** List the real identifiers and match by title:

```bash
az rest --method get \
  --url "https://api.fabric.microsoft.com/v1/admin/tenantsettings" \
  --resource "https://api.fabric.microsoft.com" \
  --query "tenantSettings[].{name:settingName, title:title}" -o table
```

Use the `name` column value as the key in `udp.yml`.

### 9.2 "Could not read tenant settings" / 403 Forbidden

**Cause:** The caller is not a Fabric administrator. Tenant settings require elevated permissions.

**Resolution:** Sign in as a **Fabric administrator**, or use a service principal granted the `Tenant.ReadWrite.All` delegated scope. The service principal must also be enabled to call admin APIs (Admin portal → *Service principals can access admin APIs used for updates*).

### 9.3 A change applied but isn't visible yet

**Cause:** Tenant setting changes propagate gradually.

**Resolution:** Wait — changes can take **up to 15 minutes** to take effect across the organization.

### 9.4 Declared security groups are ignored

**Cause:** The setting does not support per-group scoping (`canSpecifySecurityGroups` is false). `admin plan` prints a warning for this.

**Resolution:** Apply the setting org-wide (`enabled: true`/`false`) without `enabled_security_groups`.

---

## 10. Connections

### 10.1 `validate`/`diag` reports a connection as "unreachable"

**Cause:** The source `host:port` derived from the connection string or `endpoint` did not accept a TCP connection. Common reasons: the host name is wrong (`No such host is known`), a firewall or private endpoint blocks your machine, the source is paused, or the port is non-default and not specified.

**Resolution:**

- Confirm the host resolves and is reachable from where you run the command (corporate networks and CI runners are often isolated from data sources).
- For non-standard ports, include the port in the connection string (`Server=host,1500`) or endpoint URL (`https://host:8443`).
- This is a **reachability** check only — a reachable source can still reject authentication. It does not validate credentials.
- To proceed despite an unreachable source, drop `--strict`; the check is a warning by default. Use `--skip-connection-check` to skip it entirely.

### 10.2 A connection is "skipped (no connection string or endpoint to test)"

**Cause:** The connection has neither a resolvable `connection_string_var` (the environment variable is unset) nor an `endpoint`, so no `host:port` could be derived.

**Resolution:** Set the `connection_string_var` environment variable, or add an `endpoint` to the connection in `udp.yml`.

---

## 11. Getting help

- Run `udpi30 diag` to diagnose common issues.
- Check [GitHub Issues](https://github.com/microsoft/UDPi30/issues) for known bugs.
- File a new issue with the full error message and your `udp.yml` (redact secrets).
