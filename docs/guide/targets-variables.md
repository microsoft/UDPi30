# Targets and Variables

This page documents how udpi30 manages environment-specific configuration. Targets represent deployment environments (dev, staging, prod), and variables hold the values that differ between them.

---

## 1. Targets

A target is a named deployment environment. Each target maps to a specific Fabric workspace and can override variables, authentication, post-deployment checks, and deployment strategy. When you run `udpi30 deploy --target prod`, udpi30 reads the `prod` target configuration and deploys to the workspace defined there.

```yaml
targets:
  dev:
    default: true
    workspace:
      name: contoso-analytics-dev
      capacity_id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"

  staging:
    workspace:
      name: contoso-analytics-staging
      capacity_id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"

  prod:
    workspace:
      name: contoso-analytics-prod
      capacity_id: "ffffffff-gggg-hhhh-iiii-jjjjjjjjjjjj"
```

Each target can override any property from the top-level `workspace` block. Target-level settings take precedence over top-level defaults.

---

## 2. Default target

Exactly one target should have `default: true`. This is the target used when the `-t` flag is omitted:

```bash
# Uses the default target (dev in the example above)
udpi30 deploy

# Explicitly specify a target
udpi30 deploy --target prod
```

If no default is set and no `-t` flag is provided, udpi30 prompts for a target selection.

---

## 3. Variable definitions

Variables are declared at the top level with an optional description and default value. They define what configuration knobs exist in the deployment.

```yaml
variables:
  db_host:
    description: "SQL Server hostname"
    default: "localhost"

  db_port:
    description: "SQL Server port"
    default: "1433"

  enable_logging:
    description: "Enable verbose logging in notebooks"
    default: "false"
```

### 3.1 Variable forms

A variable entry is either an object with `description`/`default`, or a plain string shorthand for the value:

```yaml
variables:
  region: "eastus2"   # shorthand: equivalent to { default: "eastus2" }
```

All variable values are substituted as strings. There is no `type` field; if a variable has no default and no target override, the reference is reported as an unresolved variable at load time (a warning by default, an error with `--strict`).

---

## 4. Variable substitution syntax

Use `${...}` syntax to reference variables anywhere a string value is expected in `udp.yml`. Substitution is resolved recursively across the manifest.

| Pattern | Resolves from |
|---------|---------------|
| `${var.name}` | A variable defined in the `variables` section (default or target override) |
| `${env.NAME}` | An operating system environment variable, read at deploy time |
| `${secret.NAME}` | An environment variable treated as a secret; redacted in plan output and logs |
| `${deployment.name}`, `${deployment.version}` | Built-in deployment metadata |

### 4.1 `${var.name}` -- Deployment variables

References a variable defined in the `variables` section. The value comes from the variable's default or from a target-level override.

```yaml
connections:
  my_db:
    type: sql_server
    endpoint: "${var.db_host}:${var.db_port}"
```

### 4.2 `${env.NAME}` -- Environment variables

Reads a value from the operating system environment at deploy time. Useful for values that vary per machine or CI runner but are not secrets.

```yaml
deployment:
  name: analytics
  version: "${env.BUILD_VERSION}"
```

### 4.3 `${secret.NAME}` -- Secret references

Reads a secret from the environment. Functionally identical to `${env.NAME}` but signals intent: udpi30 redacts these values in plan output and logs. See [Secrets Management](secrets.md) for details, including the `${keyvault.VAULT.SECRET}` syntax for Azure Key Vault.

```yaml
connections:
  my_db:
    properties:
      password: "${secret.DB_PASSWORD}"
```

### 4.4 `${deployment.name}` and `${deployment.version}` -- Deployment metadata

References the deployment's own metadata. These are built-in variables, always available. Useful for generating resource names that include the project name or version.

```yaml
resources:
  lakehouses:
    data_lake:
      display_name: "${deployment.name}-lake"
      description: "Data lake for ${deployment.name} v${deployment.version}"
```

### 4.5 Combining substitutions

Substitution expressions can be combined within a single string:

```yaml
connections:
  my_db:
    endpoint: "${var.db_host}:${var.db_port}"
    properties:
      database: "${deployment.name}_${var.environment_suffix}"
```

---

## 5. Per-target variable overrides

Each target can override any variable. Target-level values take precedence over the default.

```yaml
variables:
  db_host:
    description: "SQL Server hostname"
    default: "localhost"
  environment_suffix:
    description: "Suffix for environment-specific resource names"
    default: "dev"

targets:
  dev:
    default: true
    workspace:
      name: contoso-analytics-dev
    variables:
      db_host: "dev-sql.database.windows.net"
      environment_suffix: "dev"

  staging:
    workspace:
      name: contoso-analytics-staging
    variables:
      db_host: "staging-sql.database.windows.net"
      environment_suffix: "staging"

  prod:
    workspace:
      name: contoso-analytics-prod
    variables:
      db_host: "prod-sql.database.windows.net"
      environment_suffix: "prod"
```

When deploying to `prod`, `${var.db_host}` resolves to `prod-sql.database.windows.net`.

---

## 6. `run_as` -- Service principal per target

Each target can specify a different service principal for deployment. This is useful when dev and prod workspaces are in different tenants or when you want to enforce least-privilege access.

```yaml
targets:
  dev:
    default: true
    workspace:
      name: contoso-analytics-dev
    run_as:
      service_principal: sp-udp-dev

  prod:
    workspace:
      name: contoso-analytics-prod
    run_as:
      service_principal: sp-udp-prod
```

The `service_principal` value is the display name of a service principal in Entra ID. udpi30 resolves it to a GUID via the Microsoft Graph API. When `run_as` is set, udpi30 authenticates as that service principal for the deployment. The corresponding credentials (`AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`) must be available in the environment.

If `run_as` is omitted, udpi30 uses whatever identity is available through `DefaultAzureCredential` (your logged-in account, a managed identity, or the environment variables).

---

## 7. `post_deploy` -- Validation checks

Run automated checks after a deployment succeeds. This is useful for smoke tests that verify the deployment actually works.

```yaml
targets:
  dev:
    workspace:
      name: contoso-analytics-dev
    post_deploy:
      - run: smoke_test_notebook
        expect: success
        timeout: 300

      - run: data_quality_checks
        expect: success
        timeout: 600
```

| Field | Description |
|-------|-------------|
| `run` | The resource key of a notebook or pipeline to execute |
| `expect` | Expected outcome: `success` or `completed` |
| `timeout` | Maximum wait time in seconds before the check is marked as failed |

If any `post_deploy` check fails, udpi30 reports the failure but does not roll back the deployment. Use `udpi30 rollback` to revert if needed.

---

## 8. `deployment_strategy` -- Canary deployments

For production targets, you can deploy a subset of resources first (canary) before deploying everything. If the canary resources fail, the full deployment is halted.

```yaml
targets:
  prod:
    workspace:
      name: contoso-analytics-prod
    deployment_strategy:
      type: canary
      canary_resources:
        - ingest_to_bronze
        - bronze_to_silver
      validation:
        - run: smoke_test_notebook
          expect: success
          timeout: 300
```

The canary deployment flow:

1. Deploy only the resources listed in `canary_resources`.
2. Run the `validation` checks.
3. If validation passes, deploy all remaining resources.
4. If validation fails, stop. The canary resources are rolled back.

---

## 9. Full YAML example

```yaml
deployment:
  name: contoso-analytics
  version: "2.1.0"

variables:
  db_host:
    description: "SQL Server hostname"
    default: "localhost"
  db_port:
    description: "SQL Server port"
    default: "1433"
  enable_logging:
    description: "Enable verbose notebook logging"
    default: "false"
  environment_suffix:
    description: "Environment name for resource naming"

workspace:
  capacity_id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"

resources:
  lakehouses:
    bronze:
      display_name: "${deployment.name}_bronze_${var.environment_suffix}"
      description: "Raw data landing zone"
    silver:
      display_name: "${deployment.name}_silver_${var.environment_suffix}"
      description: "Cleansed and conformed data"

  notebooks:
    ingest_to_bronze:
      display_name: "ingest_to_bronze"
      description: "Ingest raw data into the bronze lakehouse"
      path: notebooks/ingest_to_bronze.py
      default_lakehouse: bronze

connections:
  warehouse_db:
    type: sql_server
    endpoint: "${var.db_host}:${var.db_port}"
    properties:
      password: "${secret.DB_PASSWORD}"

targets:
  dev:
    default: true
    workspace:
      name: contoso-analytics-dev
    variables:
      db_host: "dev-sql.database.windows.net"
      environment_suffix: "dev"
      enable_logging: true
    post_deploy:
      - run: smoke_test_notebook
        expect: success
        timeout: 300

  staging:
    workspace:
      name: contoso-analytics-staging
    variables:
      db_host: "staging-sql.database.windows.net"
      environment_suffix: "staging"

  prod:
    workspace:
      name: contoso-analytics-prod
      capacity_id: "ffffffff-gggg-hhhh-iiii-jjjjjjjjjjjj"
    variables:
      db_host: "prod-sql.database.windows.net"
      environment_suffix: "prod"
      enable_logging: false
    run_as:
      service_principal: sp-udp-prod
    deployment_strategy:
      type: canary
      canary_resources: [ingest_to_bronze]
      validation:
        - run: smoke_test_notebook
          expect: success
          timeout: 300
    post_deploy:
      - run: data_quality_checks
        expect: success
        timeout: 600
```
