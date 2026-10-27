# Installation

This topic describes how to install, configure, and verify Unified Data Platform Deployment on a local machine or CI/CD environment.

---

## 1. Prerequisites

Before installing Unified Data Platform Deployment, confirm that the environment meets the following requirements.

### 1.1 System requirements

| Requirement | Minimum version | Notes |
|---|---|---|
| **.NET SDK** | 9.0 | Required to install and run the tool. Download from [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download). |
| **Azure CLI** | 2.50+ | Required for interactive authentication. Install from [https://aka.ms/installazurecli](https://aka.ms/installazurecli). |
| **Operating system** | Windows 10+, macOS 12+, Linux | Any OS supported by the .NET 9 runtime. |

### 1.2 Microsoft Fabric requirements

| Requirement | Details |
|---|---|
| **Fabric capacity** | An active Fabric capacity (F2 or higher). The capacity GUID is available in the Fabric admin portal. |
| **Workspace permissions** | Admin or Contributor role on the target workspace, or permission to create new workspaces. |
| **Entra ID (Azure AD) access** | The authenticated identity must have Fabric API permissions. |

> **Important**
>
> Unified Data Platform Deployment calls the Microsoft Fabric REST API. If your organization uses Conditional Access policies or tenant restrictions, confirm that API access is allowed for your identity before proceeding.

---

## 2. Install the tool

Unified Data Platform Deployment ships as a [.NET global tool](https://learn.microsoft.com/dotnet/core/tools/global-tools). Install it with the .NET SDK:

```bash
dotnet tool install --global udpi30
```

This installs the `udpi30` CLI. To use the MCP server (for AI-assisted authoring in Claude Code, Cursor, and similar tools), install the companion tool:

```bash
dotnet tool install --global udpi30-mcp
```

> **Note**
>
> Global tools are installed to `~/.dotnet/tools` (Linux/macOS) or `%USERPROFILE%\.dotnet\tools` (Windows). Ensure this directory is on your `PATH`. The .NET SDK installer normally adds it for you.

### 2.1 Install the Ontology Builder on Windows

Each GitHub release includes
[`udpi30-ontology-win-x64.zip`](https://github.com/microsoft/UDPi30/releases/latest/download/udpi30-ontology-win-x64.zip).
Extract the archive on a Windows x64 computer with the .NET 9 Desktop Runtime,
then run `Udpi30.Ontology.exe`. The app reads Fabric semantic models, creates
editable ontology entity and relationship definitions, and publishes Fabric
Ontology items.

See [Ontology Builder](../guide/ontology-builder.md) for authentication,
workflow, and current limitations.

### 2.2 Install from source

To build and install from the GitHub repository (for example, to use an unreleased feature):

```bash
git clone https://github.com/microsoft/UDPi30.git
cd udpi30/dotnet
dotnet pack -c Release
dotnet tool install --global --add-source ./src/Udpi30.Cli/bin/Release udpi30
```

The repository contains the engine (`dotnet/src/Udpi30.Core`), the CLI
(`dotnet/src/Udpi30.Cli`, built on System.CommandLine), the MCP server
(`dotnet/src/Udpi30.Mcp`), the Windows manifest editor
(`dotnet/src/Udpi30.Editor`), the Windows Ontology Builder
(`dotnet/src/Udpi30.Ontology`), and the test suite
(`dotnet/tests/Udpi30.Core.Tests`). Build the Ontology Builder from the
`dotnet/` directory with:

```powershell
dotnet build src/Udpi30.Ontology -c Release
```

---

## 3. Verify the installation

### 3.1 Check the version

```bash
udpi30 --version
```

### 3.2 Run the diagnostic check

The `diag` command validates the .NET runtime, Azure CLI status, authentication, and Fabric API connectivity in a single step:

```bash
udpi30 diag
```

**Example output (healthy environment):**

```
udpi30 diag

  ✓ .NET runtime 9.0.0
  ✓ Azure CLI installed
  ✓ Azure CLI authenticated
  ✓ Fabric API reachable
  ✓ udp.yml found
  ✓ Deployment validates

  6 passed, 0 failed
```

> **Note**
>
> The `diag` command does not require a `udp.yml` file. If one is not found, it skips the deployment validation check. All other checks still run.

---

## 4. Set up authentication

Unified Data Platform Deployment uses the `Azure.Identity` library. The credential type is selected automatically from the environment:

| Condition | Credential used |
|---|---|
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, and `AZURE_CLIENT_SECRET` all set | `ClientSecretCredential` (service principal) |
| `FABRIC_USE_BROWSER=true` | `InteractiveBrowserCredential` (browser sign-in) |
| Otherwise | `DefaultAzureCredential` (managed identity, environment, or `az login` session) |

### 4.1 Interactive login (development)

Use the Azure CLI to sign in interactively. This is the recommended method for local development.

```bash
az login
```

Then run commands against the development environment:

```bash
udpi30 plan --target dev
udpi30 deploy --target dev
```

> **Note**
>
> If you have access to multiple Azure tenants, specify the tenant explicitly: `az login --tenant your-tenant-id`.

### 4.2 Service principal (CI/CD)

For automated pipelines, use a service principal with the following environment variables:

```bash
export AZURE_TENANT_ID="your-tenant-id"
export AZURE_CLIENT_ID="your-client-id"
export AZURE_CLIENT_SECRET="your-client-secret"
```

When all three variables are set, the tool authenticates with `ClientSecretCredential`. Run commands non-interactively using the `--auto-approve` flag:

```bash
udpi30 deploy --target prod --auto-approve
```

**To set up a service principal:**

1. Register an application in Entra ID (Azure AD).
2. Create a client secret or configure certificate-based auth.
3. Grant the service principal the **Contributor** role on the target Fabric workspace.
4. If creating workspaces, grant the service principal permission to create workspaces in the Fabric tenant.

> **Warning**
>
> Never commit service principal credentials to source control. Use your CI/CD platform's secret management (for example, GitHub Actions secrets, Azure DevOps variable groups, or Azure Key Vault) to inject these values at runtime.

### 4.3 GitHub Actions example

```yaml
# .github/workflows/deploy.yml
name: Deploy Fabric Deployment
on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - run: dotnet tool install --global udpi30

      - run: udpi30 deploy --target prod --auto-approve
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
          AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
          AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}
```

See [GitHub Actions](../cicd/github-actions.md) and [Azure DevOps](../cicd/azure-devops.md) for complete pipeline templates.

---

## 5. Upgrading

### 5.1 Check for updates

```bash
udpi30 check-update
```

### 5.2 Upgrade to the latest version

```bash
dotnet tool update --global udpi30
```

### 5.3 Pin a specific version

For reproducible CI/CD pipelines, pin the version when installing:

```bash
dotnet tool install --global udpi30 --version 1.14.0
```

Or commit a [tool manifest](https://learn.microsoft.com/dotnet/core/tools/local-tools) (`.config/dotnet-tools.json`) and run `dotnet tool restore`.

---

## 6. Troubleshooting installation issues

### 6.1 `udpi30: command not found`

**Cause:** The .NET global tools directory is not on the `PATH`.

**Resolution:** Add it to the shell profile.

```bash
# macOS / Linux (~/.zshrc or ~/.bashrc)
export PATH="$PATH:$HOME/.dotnet/tools"
```

```powershell
# Windows (PowerShell profile)
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
```

### 6.2 `az login` errors

**Cause:** The Azure CLI is not installed or not authenticated.

**Resolution:**

```bash
az --version           # confirm the CLI is installed (see https://aka.ms/installazurecli)
az login               # sign in
az account show --query '{name:name, tenantId:tenantId}' -o table
```

### 6.3 Proxy or firewall issues

If you are behind a corporate proxy, configure NuGet and the Azure CLI:

```bash
export HTTPS_PROXY=http://proxy.example.com:8080
dotnet tool install --global udpi30
az login
```

If your firewall blocks nuget.org, install from a private NuGet feed with `--add-source <feed-url>`.

---

## 7. Next steps

- [Quick start tutorial](../getting-started/quickstart.md) -- Create and deploy your first deployment.
- [CLI commands reference](../cli/commands.md) -- Full reference for all `udpi30` commands.
- [udp.yml reference](../guide/udp-yml.md) -- Complete schema reference for deployment definitions.
