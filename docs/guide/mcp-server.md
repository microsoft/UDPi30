# MCP Server

This page covers installing, configuring, and using udpi30 as an MCP (Model Context Protocol) server in GitHub Copilot, Claude Code, Claude Desktop, or any MCP-compatible client. The MCP server lets you manage Fabric workspaces conversationally through 14 tools.

---

## 1. Install

The MCP server ships as a .NET global tool:

```bash
dotnet tool install --global udpi30-mcp
```

---

## 2. Configure

### 2.1 GitHub Copilot

Add to your project's `.vscode/mcp.json`:

```json
{
  "servers": {
    "udpi30": {
      "command": "udpi30-mcp"
    }
  }
}
```

Or add to your VS Code user settings (`settings.json`) under `"mcp.servers"` to use across all projects.

### 2.2 Claude Code

Add to your project's `.claude/settings.json`:

```json
{
  "mcpServers": {
    "udpi30": {
      "command": "udpi30-mcp"
    }
  }
}
```

Or add globally to `~/.claude/settings.json` to use across all projects.

### 2.3 Claude Desktop

Add to `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "udpi30": {
      "command": "udpi30-mcp"
    }
  }
}
```

---

## 3. Prerequisites

| Requirement | Detail |
|---|---|
| .NET SDK | 9.0 or later (required for the global tool) |
| Azure CLI | 2.50 or later, authenticated with `az login` |
| Fabric capacity | Active capacity (F2 or larger) |
| Workspace role | Admin or Contributor on the target workspace |
| Project files | `udp.yml` present in your project directory |

Run `udpi30 diag` to verify everything is configured correctly.

---

## 4. Available tools

The server exposes 14 tools:

| Tool | Description | Example prompt |
|------|-------------|----------------|
| `udp_validate` | Validate udp.yml | "Validate my Fabric deployment" |
| `udp_plan` | Preview deployment changes | "What would change if I deploy to dev?" |
| `udp_deploy` | Deploy to a target | "Deploy to dev" |
| `udp_destroy` | Tear down resources | "Destroy the test environment" |
| `udp_status` | Show deployed resources | "What's deployed in prod?" |
| `udp_drift` | Detect out-of-band changes | "Check for drift in staging" |
| `udp_run` | Run a notebook or pipeline | "Run the ingest_to_bronze notebook in dev" |
| `udp_history` | Show deployment history | "Show me recent deployments" |
| `udp_diag` | Diagnose issues | "Check if my Fabric setup is working" |
| `udp_list_templates` | List templates | "What templates are available?" |
| `udp_list_workspaces` | List workspaces | "Show me all Fabric workspaces" |
| `udp_list_capacities` | List capacities | "What Fabric capacities do I have?" |
| `udp_export` | Export items from a workspace | "Export the prod workspace items" |
| `udp_generate` | Generate udp.yml from a workspace (optionally importing deployed Entra/Azure resources via `include_entra` / `include_azure`) | "Generate a project from my existing workspace, including its Entra groups and Azure resources" |

---

## 5. Example conversations

**"What's in my Fabric workspace?"**
> The AI calls `udp_status` and responds:
>
> *"Your dev workspace has 12 items deployed: 3 lakehouses (bronze, silver, gold), 3 notebooks (ingest_to_bronze, transform_to_silver, aggregate_to_gold), 2 pipelines (daily_etl, weekly_refresh), 1 Spark environment, 1 warehouse, 1 SQL analytics endpoint, and 1 data agent. Last deployment was 3 hours ago. No drift detected."*

**"Deploy my project to dev"**
> The AI first calls `udp_validate` to check your deployment, then `udp_plan` to preview changes:
>
> *"Your udp.yml is valid. Here's what will happen: 3 lakehouses will be created, 3 notebooks uploaded, and 2 pipelines configured with schedules. Total: 8 new resources. No existing resources will be modified. Should I proceed with the deployment?"*
>
> After you confirm, it calls `udp_deploy` and reports:
>
> *"Deployed 8 resources to sales-analytics-dev in 45 seconds. All items created successfully. You can open the workspace at https://app.udp.microsoft.com/..."*

**"Something changed in prod that I didn't deploy"**
> The AI calls `udp_drift` and responds:
>
> *"I found drift in 2 items. A notebook 'manual_fix' was added directly in the portal (not in your udp.yml). And the 'daily_etl' pipeline schedule was changed from 6:00 AM to 8:00 AM. Would you like me to update your udp.yml to match, or redeploy to revert the changes?"*

**"Run the ETL pipeline"**
> The AI calls `udp_run` and responds:
>
> *"I've submitted the 'daily_etl' pipeline in the dev workspace. Job ID: a3f8c... You can monitor progress in the Fabric portal under Monitoring Hub. I'll check back -- want me to poll the status?"*

**"Set up a new Fabric project"**
> The AI calls `udp_list_capacities` and `udp_list_templates`, then responds:
>
> *"You have 2 capacities available: 'Team-Dev' (F8, West US 2) and 'Prod' (F16, East US). I found 4 templates: medallion (bronze/silver/gold lakehouse), starter (single lakehouse + notebook), warehouse (SQL-first with views), and custom (empty scaffold). Which template and capacity would you like to use?"*

---

## 6. Setting up both MCP servers

For full coverage, use udpi30-mcp alongside [Microsoft's Fabric MCP server](https://github.com/microsoft/mcp). udpi30-mcp lets the AI act on your workspace (deploy, plan, status), while Microsoft's Fabric MCP gives the AI knowledge of Fabric APIs and best practices.

### 6.1 GitHub Copilot (`.vscode/mcp.json`)

```json
{
  "servers": {
    "udpi30": {
      "command": "udpi30-mcp"
    },
    "udp": {
      "command": "npx",
      "args": ["-y", "@anthropic-ai/mcp-server-fetch", "https://github.com/microsoft/mcp"]
    }
  }
}
```

> Check Microsoft's [Fabric MCP repo](https://github.com/microsoft/mcp) for the latest install command -- the `args` above are illustrative.

### 6.2 Claude Code (`.claude/settings.json`)

```json
{
  "mcpServers": {
    "udpi30": {
      "command": "udpi30-mcp"
    },
    "udp": {
      "command": "npx",
      "args": ["-y", "@anthropic-ai/mcp-server-fetch", "https://github.com/microsoft/mcp"]
    }
  }
}
```

### 6.3 Claude Desktop (`claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "udpi30": {
      "command": "udpi30-mcp"
    },
    "udp": {
      "command": "npx",
      "args": ["-y", "@anthropic-ai/mcp-server-fetch", "https://github.com/microsoft/mcp"]
    }
  }
}
```

### 6.4 What each server provides

| MCP Server | Purpose | Example |
|------------|---------|---------|
| udpi30-mcp | Manage your Fabric project: deploy, plan, status, run, drift, destroy | "Deploy to dev", "Check for drift" |
| Microsoft Fabric MCP | Fabric API docs, best practices, item schemas | "How do I configure a pipeline trigger?", "What Spark versions does Fabric support?" |

Together, the AI can both *understand* Fabric and *act* on your workspace.

---

## 7. Troubleshooting

| Symptom | Resolution |
|---|---|
| `udpi30-mcp: command not found` | Confirm you installed with `dotnet tool install --global udpi30-mcp`. Check that the .NET global tools directory (`~/.dotnet/tools` on macOS/Linux, `%USERPROFILE%\.dotnet\tools` on Windows) is in your PATH. |
| Authentication error | Run `az login` in your terminal first, or set the `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, and `AZURE_CLIENT_SECRET` environment variables for service principal authentication. |
| Tools not showing up | Restart your IDE after adding the MCP configuration. Check the MCP server logs for errors. |
