# Environment Variables

This page is the reference for environment variables recognized by `udpi30`, and for the variable syntaxes that read from the environment inside `udp.yml`.

---

## 1. Recognized environment variables

### 1.1 Authentication

| Variable | Required | Description |
|---|---|---|
| `AZURE_TENANT_ID` | For SP auth | Entra ID (Azure AD) tenant GUID |
| `AZURE_CLIENT_ID` | For SP auth | Service principal application (client) ID |
| `AZURE_CLIENT_SECRET` | For SP auth | Service principal client secret |
| `FABRIC_USE_BROWSER` | No | Set to `true` to force interactive browser sign-in |

Credential selection (handled by `FabricAuth` via `Azure.Identity`) follows this order:

| Condition | Credential used |
|---|---|
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, and `AZURE_CLIENT_SECRET` all set | `ClientSecretCredential` |
| `FABRIC_USE_BROWSER=true` | `InteractiveBrowserCredential` |
| Otherwise | `DefaultAzureCredential` (managed identity, environment, or `az login` session) |

### 1.2 Fabric

| Variable | Required | Description |
|---|---|---|
| `FABRIC_CAPACITY_ID` | No | Capacity GUID used for workspace creation during `deploy` and `init`, as an alternative to setting `capacity_id` in `udp.yml`. |

### 1.3 Storage (remote state)

| Variable | Required | Description |
|---|---|---|
| `AZURE_STORAGE_ACCOUNT_NAME` | For `azureblob` / `adls` backends | Storage account name for the Azure Blob or ADLS Gen2 state backend. |

---

## 2. Environment variables in udp.yml

Reference any environment variable in `udp.yml` with `${env.VARIABLE_NAME}`:

```yaml
resources:
  notebooks:
    etl:
      path: ./notebooks/etl.py
      parameters:
        db_host: "${env.DB_HOST}"
```

---

## 3. Secret variables

Reference with `${secret.NAME}`:

```yaml
targets:
  prod:
    variables:
      db_password: "${secret.DB_PASSWORD}"
```

Secrets are resolved by the `SecretsResolver` from environment variables at deploy time. The `secret.` prefix is a convention; it reads from the same environment as `${env.NAME}` but marks the value as sensitive.

---

## 4. Key Vault variables

Reference with `${keyvault.VAULT_NAME.SECRET_NAME}`:

```yaml
connections:
  my_db:
    properties:
      password: "${keyvault.udp-vault.db-password}"
```

Key Vault secrets are fetched through the Azure Key Vault `SecretClient` and cached for the duration of the run. No additional installation is required; the Azure SDK is built into the tool. The lookup authenticates with the same credential chain described in section 1.1, so the identity must have secret read permission on the vault.

---

## 5. Built-in deployment variables

The following variables are always available in `udp.yml`:

| Variable | Value |
|---|---|
| `${deployment.name}` | Deployment name from `deployment.name` |
| `${deployment.version}` | Deployment version from `deployment.version` |

---

## 6. State backend configuration

Deployment state (`deployment-state.json`) can be stored locally or in a remote backend. Configure the backend in `udp.yml`:

```yaml
state:
  backend: onelake  # or: azureblob, adls, local
  config:
    workspace_id: "guid"      # OneLake
    lakehouse_id: "guid"      # OneLake
    account_name: "storage"   # azureblob / adls
    container_name: "state"   # azureblob
    filesystem: "state"       # adls
    account_key: "..."        # azureblob (optional, uses DefaultAzureCredential)
```

| Backend | Storage | Status |
|---|---|---|
| `local` | JSON file on the local filesystem | Stable |
| `azureblob` | Azure Blob Storage | Beta |
| `onelake` / `adls` | OneLake / ADLS Gen2 | Beta |

> **Note**
>
> For the Blob and ADLS backends, omit `account_key` where possible so the tool authenticates with `DefaultAzureCredential` instead of a shared key. The account name can also be supplied through the `AZURE_STORAGE_ACCOUNT_NAME` environment variable.
