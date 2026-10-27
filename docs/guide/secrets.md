# Secrets Management

This page describes how udpi30 injects secrets such as database passwords, API keys, and connection strings into a deployment at deploy time. Secrets never appear in plain text in `udp.yml` and are never committed to source control.

---

## 1. Resolution mechanisms

udpi30 supports two secret reference syntaxes, resolved by the `SecretsResolver` class in `Udpi30.Core`. Resolution is recursive across the entire manifest: any string value in the resolved configuration may contain a reference.

| Syntax | Source | Notes |
|--------|--------|-------|
| `${secret.NAME}` | Environment variable `NAME` | Read at deploy time. Deployment fails if the variable is not set. |
| `${keyvault.VAULT_NAME.SECRET_NAME}` | Azure Key Vault | Retrieved through the Azure Key Vault `SecretClient`. Results are cached for the duration of the run. |

In addition to secret references, two built-in variables are always available: `${deployment.name}` and `${deployment.version}`.

---

## 2. Environment variable secrets

Use the `${secret.NAME}` syntax to reference an environment variable. At deploy time, udpi30 reads the value from the environment and substitutes it into the configuration.

```yaml
connections:
  my_database:
    type: sql_server
    endpoint: "${secret.DB_HOST}"
    properties:
      username: "${secret.DB_USERNAME}"
      password: "${secret.DB_PASSWORD}"

  my_api:
    type: http
    endpoint: "https://api.example.com"
    properties:
      api_key: "${secret.API_KEY}"
```

Before deploying, set the environment variables:

```bash
export DB_HOST="myserver.database.windows.net"
export DB_USERNAME="svc_udp"
export DB_PASSWORD="correct-horse-battery-staple"
export API_KEY="sk-abc123..."

udpi30 deploy --target prod -y
```

If a referenced secret is missing from the environment, udpi30 fails with a clear error before making any API calls:

```
Error: Secret 'DB_PASSWORD' is not set.
  Set the environment variable DB_PASSWORD or use a KeyVault reference.
```

---

## 3. Azure Key Vault integration

For teams that manage secrets centrally in Azure Key Vault, use the `${keyvault.VAULT_NAME.SECRET_NAME}` syntax. udpi30 retrieves the secret value from Key Vault at deploy time using the Azure Key Vault `SecretClient` and caches the result for the remainder of the run.

```yaml
connections:
  my_database:
    type: sql_server
    endpoint: "${keyvault.contoso-kv.db-host}"
    properties:
      username: "${keyvault.contoso-kv.db-username}"
      password: "${keyvault.contoso-kv.db-password}"

  my_api:
    type: http
    endpoint: "https://api.example.com"
    properties:
      api_key: "${keyvault.contoso-kv.api-key}"
```

### 3.1 Prerequisites

1. Install udpi30 as a .NET global tool. Key Vault support is included; no additional component is required:

    ```bash
    dotnet tool install --global udpi30
    ```

2. Grant the deploying identity (your user account or the CI/CD service principal) the **Key Vault Secrets User** role on the Key Vault, or assign a Key Vault access policy with **Get** permission on secrets.

3. The Key Vault must be network-accessible from the machine running `udpi30`. If the vault uses private endpoints, ensure the CI/CD runner can reach it.

Key Vault access uses `DefaultAzureCredential` from `Azure.Identity`, which honors the service principal environment variables (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`), managed identity, or an active `az login` session.

Secret references always resolve to the current (latest) version of the secret. Pinning a specific secret version is not supported; to roll back a secret, restore the previous value in Key Vault.

---

## 4. Resolution at deploy time

Secret resolution happens during the plan and deploy phases, after the YAML is parsed but before any API calls are made.

1. The `SecretsResolver` scans all string values in the resolved configuration for `${secret.*}` and `${keyvault.*}` patterns. Resolution is recursive, so references inside resolved values are also processed.
2. For `${secret.NAME}`, it reads the environment variable `NAME`. If the variable is not set, deployment fails.
3. For `${keyvault.VAULT.SECRET}`, it calls the Azure Key Vault API to retrieve the secret value and caches the result. If the vault or secret does not exist, or if the identity lacks permission, deployment fails.
4. The resolved values are used in the Fabric API calls but are never written to the state file, logs, or plan output. udpi30 redacts secret values in all output.

---

## 5. Secrets in CI/CD

### 5.1 GitHub Actions

Store secrets in your repository settings (**Settings > Secrets and variables > Actions**) or at the environment level. Reference them as environment variables in your workflow:

```yaml
- name: Deploy to production
  run: udpi30 deploy --target prod -y
  env:
    AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
    AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
    AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}
    DB_HOST: ${{ secrets.DB_HOST }}
    DB_USERNAME: ${{ secrets.DB_USERNAME }}
    DB_PASSWORD: ${{ secrets.DB_PASSWORD }}
    API_KEY: ${{ secrets.API_KEY }}
```

GitHub Actions automatically masks secret values in logs. Combined with udpi30's own redaction, secrets do not appear in workflow output.

If you use Key Vault references instead of environment variable secrets, only the three Azure authentication secrets are required. udpi30 retrieves everything else from Key Vault.

### 5.2 Azure DevOps

Store secrets in a variable group (**Pipelines > Library**). Link the variable group to your pipeline and mark sensitive values as secret:

```yaml
variables:
  - group: udp-credentials
  - group: udp-secrets  # Contains DB_HOST, DB_PASSWORD, etc.

steps:
  - script: udpi30 deploy --target prod -y
    displayName: 'Deploy to production'
    env:
      AZURE_TENANT_ID: $(AZURE_TENANT_ID)
      AZURE_CLIENT_ID: $(AZURE_CLIENT_ID)
      AZURE_CLIENT_SECRET: $(AZURE_CLIENT_SECRET)
      DB_HOST: $(DB_HOST)
      DB_PASSWORD: $(DB_PASSWORD)
```

Azure DevOps variable groups can be linked directly to an Azure Key Vault, which provides automatic secret rotation without updating pipeline variables.

---

## 6. Best practices

| Practice | Rationale |
|----------|-----------|
| Never commit secrets to source control. | Even if removed later, secrets remain in git history. |
| Use `.gitignore` to prevent accidental commits. | The `udpi30 init` command generates a `.gitignore` with the common patterns shown below. |
| Prefer Key Vault over environment variables for production. | Key Vault provides audit logging, access policies, automatic rotation, and centralized management. Environment variables are simpler for development but harder to audit and rotate. |
| Use separate secrets per environment. | Do not share database credentials between dev and prod. Use per-target variables that reference different secrets. |
| Rotate secrets regularly. | Set calendar reminders for service principal client secret expiry. Key Vault secrets can be configured with expiry dates and rotation policies. |

The generated `.gitignore` includes:

```gitignore
# udpi30 state (contains workspace IDs, item IDs)
.udpi30/

# Environment files with secrets
.env
.env.*

# Azure credentials
*.pem
*.key
```

Per-target secret references look like this:

```yaml
targets:
  dev:
    variables:
      db_password: "${secret.DEV_DB_PASSWORD}"
  prod:
    variables:
      db_password: "${secret.PROD_DB_PASSWORD}"
```

---

## 7. Full YAML example

```yaml
deployment:
  name: contoso-analytics

variables:
  db_host:
    description: "SQL Server hostname"
  db_password:
    description: "SQL Server password"

connections:
  warehouse_db:
    type: sql_server
    endpoint: "${var.db_host}"
    properties:
      username: "${keyvault.contoso-kv.db-username}"
      password: "${var.db_password}"

  external_api:
    type: http
    endpoint: "https://api.partner.com/v2"
    properties:
      api_key: "${keyvault.contoso-kv.partner-api-key}"

targets:
  dev:
    default: true
    workspace:
      name: contoso-analytics-dev
    variables:
      db_host: "dev-sql.database.windows.net"
      db_password: "${secret.DEV_DB_PASSWORD}"

  prod:
    workspace:
      name: contoso-analytics-prod
    variables:
      db_host: "prod-sql.database.windows.net"
      db_password: "${keyvault.contoso-kv.prod-db-password}"
```

In this example, dev uses an environment variable for the database password (simple for local development), while prod retrieves it from Key Vault (auditable, centrally managed).
