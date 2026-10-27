# Instructions for Fabric Projects

This project uses **Unified Data Platform Deployment** (`udpi30`) to manage Microsoft Fabric, Azure Data Services and Databricks resources.

## MCP Server

If configured, you have access to these tools via the `udpi30-mcp` MCP server:

- `udp_validate` — Validate the udp.yml deployment
- `udp_plan` — Preview what would change (dry-run)
- `udp_deploy` — Deploy resources to a target workspace
- `udp_destroy` — Tear down resources
- `udp_status` — Show deployed resource health
- `udp_drift` — Detect out-of-band changes
- `udp_run` — Run a notebook or pipeline
- `udp_history` — Show deployment history
- `udp_diag` — Diagnose configuration issues
- `udp_list_templates` — Available project templates
- `udp_list_workspaces` — List Fabric workspaces
- `udp_list_capacities` — List available capacities

## Key Files

- `udp.yml` — Platform definition (lakehouses, notebooks, pipelines, security, targets)
- `notebooks/` — PySpark notebooks deployed to Fabric
- `sql/` — SQL scripts executed on warehouses
- `agent/` — Data Agent instructions and few-shot examples

## Common Tasks

- **Deploy to dev:** Use `udp_deploy` with `target: "dev"` or run `udpi30 deploy --target dev`
- **Check what's deployed:** Use `udp_status` with `target: "dev"`
- **Preview changes:** Use `udp_plan` with `target: "dev"`
- **Run a notebook:** Use `udp_run` with `resource_name: "notebook_name"` and `target: "dev"`

## udp.yml Structure

```yaml
deployment:
  name: project-name
  version: "1.0.0"

resources:
  lakehouses: {}    # Data storage
  notebooks: {}     # PySpark ETL code
  pipelines: {}     # Orchestration
  environments: {}  # Spark runtime + libraries
  warehouses: {}    # SQL analytics
  data_agents: {}   # Natural language interface

security:
  roles: []         # Workspace + OneLake access

targets:
  dev: {}           # Dev workspace config
  test: {}          # Test workspace config
  prod: {}          # Prod workspace config
```

## Prerequisites

- `az login` for authentication
- Fabric capacity must be active
- `dotnet tool install --global udpi30-mcp` for MCP tools
