# udpi30-mcp

**MCP server for Microsoft Unified Data Platform (Fabric) deployments.** Manage your Fabric workspaces conversationally from GitHub Copilot, Claude Code, Claude Desktop, or any MCP-compatible client.

This is the [Model Context Protocol](https://modelcontextprotocol.io) companion to the [`udpi30`](https://www.nuget.org/packages/udpi30/) CLI. It exposes the same declarative deploy/plan/drift workflow as **14 tools** your AI assistant can call — so you can just say *"Deploy to dev"* or *"Check for drift in prod"*.

## Install

```bash
dotnet tool install --global udpi30-mcp
```

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/). Authenticate to Azure with `az login` (or a service principal) before use.

## Configure

**GitHub Copilot** — `.vscode/mcp.json`:

```json
{
  "servers": {
    "udpi30": { "command": "udpi30-mcp" }
  }
}
```

**Claude Code** — `.claude/settings.json` (or `~/.claude/settings.json` for all projects):

```json
{
  "mcpServers": {
    "udpi30": { "command": "udpi30-mcp" }
  }
}
```

**Claude Desktop** — `claude_desktop_config.json` (`%APPDATA%\Claude\` on Windows, `~/Library/Application Support/Claude/` on macOS):

```json
{
  "mcpServers": {
    "udpi30": { "command": "udpi30-mcp" }
  }
}
```

Restart your IDE after adding the configuration.

## Tools

| Tool | Description |
|------|-------------|
| `udp_validate` | Validate a `udp.yml` — schema, references, dependencies, naming, policies |
| `udp_plan` | Preview create/update/delete actions without making changes |
| `udp_deploy` | Deploy to a target (plan first; `confirm: true` to execute) |
| `udp_destroy` | Tear down deployment-managed resources |
| `udp_status` | Show deployed resource health, item IDs, and last deploy time |
| `udp_drift` | Detect out-of-band changes between deployed state and live workspace |
| `udp_run` | Run a notebook or pipeline in the workspace |
| `udp_history` | Show deployment history for a target |
| `udp_diag` | Diagnose .NET runtime, Azure auth, Fabric API, and deployment issues |
| `udp_list_templates` | List available project templates |
| `udp_list_workspaces` | List accessible Fabric workspaces |
| `udp_list_capacities` | List Fabric capacities with IDs, SKUs, and regions |
| `udp_export` | Export item definitions from a deployed workspace to local files |
| `udp_generate` | Generate a `udp.yml` from an existing workspace (optionally importing deployed Entra/Azure resources via `include_entra` / `include_azure`) |

## Example

> **You:** "Deploy my project to dev."
>
> The assistant calls `udp_validate`, then `udp_plan`:
> *"Your udp.yml is valid. 3 lakehouses will be created, 3 notebooks uploaded, and 2 pipelines configured. 8 new resources, none modified. Proceed?"*
>
> After you confirm, it calls `udp_deploy`:
> *"Deployed 8 resources to sales-analytics-dev in 45 seconds."*

## Prerequisites

| Requirement | Detail |
|---|---|
| .NET SDK | 9.0 or later |
| Azure CLI | Authenticated with `az login` (or service principal env vars) |
| Fabric capacity | Active (F2 or larger) |
| Workspace role | Admin or Contributor on the target workspace |
| Project files | A `udp.yml` in your project directory |

## Links

- **Docs:** https://microsoft.github.io/UDPi30/guide/mcp-server/
- **CLI package:** https://www.nuget.org/packages/udpi30/
- **Source & issues:** https://github.com/microsoft/UDPi30

## License

MIT
