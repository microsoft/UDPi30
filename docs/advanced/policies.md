# Policy Enforcement

This page describes pre-deploy validation rules (policies) that catch problems before they reach a workspace. Policies are defined in the `policies` section of `udp.yml` and are evaluated every time you run `udpi30 validate` or `udpi30 deploy`; if any policy is violated, the command exits with a non-zero status and prints the violations, preventing a bad deployment from going through.

!!! warning "Experimental feature"
    Policy enforcement is **Experimental**. The feature is built but has not been validated in production use. Behavior and configuration syntax may change in future releases.

---

## 1. Defining policies

Add a `policies` block to your `udp.yml`:

```yaml
policies:
  require_description: true
  naming_convention: snake_case
  max_notebook_size_kb: 500
  blocked_libraries:
    - "pandas<2.0"
    - "numpy<1.24"
```

Policies apply to all resources in the deployment. They are checked during validation, before any API calls are made.

---

## 2. Built-in policy options

| Policy | Type | Purpose |
|--------|------|---------|
| `require_description` | Boolean | Require a non-empty `description` on every resource |
| `naming_convention` | String or object | Enforce a naming pattern for resource keys |
| `max_notebook_size_kb` | Integer | Cap notebook definition file size |
| `blocked_libraries` | List of specifiers | Block deployments that declare specific library versions |

### 2.1 `require_description`

Requires every resource to have a non-empty `description` field. This enforces documentation discipline across the project.

```yaml
policies:
  require_description: true
```

**Violation example:**

```
POLICY VIOLATION: require_description
  Resource 'ingest_to_bronze' (Notebook) is missing a description.
  Resource 'silver' (Lakehouse) is missing a description.
```

### 2.2 `naming_convention`

Enforces a naming pattern for all resource keys. The only supported value is `snake_case` (pattern `^[a-z][a-z0-9_]*$`: lowercase, starts with a letter, underscores allowed):

```yaml
policies:
  naming_convention: snake_case
```

**Violation example:**

```
POLICY VIOLATION: naming_convention (expected: snake_case)
  Resource 'IngestToBronze' does not match snake_case.
  Resource 'udp-lakehouse' does not match snake_case.
```

Other conventions (kebab-case, camelCase) and custom regex patterns are not supported.

### 2.3 `max_notebook_size_kb`

Sets a maximum file size for notebook definitions in kilobytes. This prevents accidentally committing notebooks with large embedded outputs or data.

```yaml
policies:
  max_notebook_size_kb: 500
```

**Violation example:**

```
POLICY VIOLATION: max_notebook_size_kb (limit: 500 KB)
  Notebook 'exploration' is 2,340 KB. Strip outputs before committing.
```

### 2.4 `blocked_libraries`

Prevents deployments whose Spark environments declare specific libraries (Fabric Spark environments declare Python library dependencies).

```yaml
policies:
  blocked_libraries:
    - pandas
    - requests
```

This policy inspects `environment` resources that declare library dependencies. Matching is by **library name prefix**: any version-specifier suffix (`<`, `>`, `=`) in the blocked entry is stripped, and any declared library whose name starts with the result fails validation. Writing `pandas<2.0` therefore blocks every pandas version, not just versions below 2.0 — list bare library names to avoid confusion.

**Violation example:**

```
POLICY VIOLATION: blocked_libraries
  Environment 'spark_env' uses blocked library 'pandas==1.5.3'.
```

---

## 3. Custom policy rules

The `policies` schema accepts a `rules` list for project-specific requirements:

```yaml
policies:
  rules:
    - name: max_resources
      check: max_resources
      value: 50
      severity: error
```

Each rule has a `name`, a `check` identifier, an optional `value`, and a `severity` (`error` by default).

**Custom rules are parsed but not yet enforced.** The policy engine currently evaluates only the four built-in options in section 2; entries in `rules` are accepted by the schema and ignored at validation time. Treat custom rules as a forward-compatible placeholder until enforcement ships.

---

## 4. Running policy checks

### 4.1 During validation

```bash
udpi30 validate
```

Validation always runs policies. If any policy is violated, the command exits with code `1` and prints all violations.

### 4.2 Strict mode

The `--strict` flag promotes warnings to errors, including unresolved-variable warnings:

```bash
udpi30 validate --strict
```

This is recommended for CI/CD pipelines where you want zero tolerance for policy issues.

### 4.3 During deployment

`udpi30 deploy` runs validation automatically before making any API calls. If validation fails, deployment is aborted. You do not need to run `validate` separately before `deploy`.

---

## 5. Full example output

```
$ udpi30 validate --strict

Validating deployment: contoso-analytics (v1.0.0)

  Checking schema...                     OK
  Checking resource references...        OK
  Checking dependency cycles...          OK
  Checking policies...                   FAILED

  POLICY VIOLATIONS (3):

    1. require_description
       Resource 'staging' (Lakehouse) is missing a description.

    2. naming_convention (expected: snake_case)
       Resource 'MyNotebook' does not match snake_case.

    3. blocked_libraries
       Environment 'spark_env' declares 'pandas==1.5.3',
       which matches blocked specifier 'pandas<2.0'.

Validation failed: 3 policy violations.
```

---

## 6. CI/CD integration

Use policy validation as a gate in your deployment pipeline. The non-zero exit code on failure will cause the CI job to fail, preventing the merge or deployment.

### 6.1 GitHub Actions

```yaml
- name: Validate deployment
  run: udpi30 validate --strict
```

### 6.2 Azure DevOps

```yaml
- script: udpi30 validate --strict
  displayName: 'Validate deployment (strict)'
```

### 6.3 Pull request workflow

A typical pattern is to run `udpi30 validate --strict` on every pull request so that policy violations are caught before code is merged:

```yaml
name: PR Validation

on:
  pull_request:
    paths:
      - 'udp.yml'
      - 'notebooks/**'
      - 'sql/**'

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - run: dotnet tool install --global udpi30

      - name: Validate with strict policies
        run: udpi30 validate --strict
```

No secrets are required for validation because it does not contact the Fabric API. It only parses and checks the local deployment definition.

---

## 7. Recommended policy configuration

A solid starting point for most teams:

```yaml
policies:
  require_description: true
  naming_convention: snake_case
  max_notebook_size_kb: 500
  blocked_libraries:
    - "pandas<2.0"
```

Revisit the configuration as your project grows and you identify patterns that should be enforced organization-wide.
