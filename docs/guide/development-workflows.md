# Development Workflows

This page describes five development patterns for building Microsoft Fabric projects with udpi30, from fully local authoring to conversational MCP-driven development. Use the comparison in section 6 and the recommendations in section 7 to choose the pattern that fits your team.

---

## 1. Pattern 1: Write Local

Best for: new projects, small teams, developers comfortable with code editors.

You write notebooks and `udp.yml` locally, test via `udpi30 deploy`, and iterate.

```
┌─────────────────────────────────────────────────┐
│ Local Machine                                   │
│                                                 │
│  1. udpi30 init --template medallion        │
│  2. Edit notebooks in VS Code / your editor     │
│  3. udpi30 validate                         │
│  4. udpi30 deploy --target dev                    │
│  5. Open Fabric portal → run notebook → verify  │
│  6. Fix issues locally → redeploy               │
│  7. git commit + push → CI/CD to test/prod      │
└─────────────────────────────────────────────────┘
```

### 1.1 How it works

```bash
# Create project
udpi30 init --template medallion --name udp-project
cd udp-project

# Edit udp.yml — set your capacity_id and workspace names
# Edit notebooks in ./notebooks/ with your editor

# Deploy to dev to test
udpi30 deploy --target dev

# Open Fabric portal — your notebooks are there
# Run them, check the output, verify data in lakehouses

# Something broken? Fix locally, redeploy
udpi30 deploy --target dev

# Working? Commit and push
git add -A && git commit -m "feat: add ETL pipeline"
git push  # CI/CD deploys to test → prod
```

### 1.2 Limitations

- Notebooks are uploaded as-is; there is no Spark intellisense while editing locally.
- You cannot run notebooks locally (they need Fabric Spark).
- Testing means deploying to dev and running in the portal.
- Best for simple notebooks; complex Spark code benefits from portal development.

### 1.3 When to use

- Greenfield projects (no existing workspace).
- Teams that prefer code-first development.
- CI/CD-heavy workflows where everything is in git.

---

## 2. Pattern 2: Develop in Portal, Export to Git

Best for: most teams. Developers use the full Fabric portal experience, then capture state for CI/CD.

```
┌─────────────────────────────────────────────────┐
│ Fabric Portal (dev workspace)                   │
│                                                 │
│  1. Create/edit notebooks in the portal         │
│  2. Run notebooks, test with real data          │
│  3. Build pipelines, configure schedules        │
│  4. Everything working? Export:                 │
│                                                 │
│     udpi30 generate --workspace "udp-dev"    │
│                                                 │
│  5. Commit udp.yml + notebooks to git        │
│  6. CI/CD deploys to test → prod                │
│  7. Future changes: edit in portal → re-export  │
└─────────────────────────────────────────────────┘
```

The export step uses reverse generation: `udpi30 generate` scans the workspace through the Fabric REST API and emits `udp.yml` plus the source files for each item.

### 2.1 How it works

```bash
# Step 1: Develop in the Fabric portal
# - Create lakehouses, notebooks, pipelines manually
# - Run notebooks, test with real data
# - Iterate until everything works

# Step 2: Export the workspace to udp.yml
udpi30 generate --workspace "udp-dev-workspace"
# Creates:
#   udp.yml          — all resources declared
#   notebooks/*.py      — exported notebook content
#   pipelines/*.json    — pipeline definitions

# Step 3: Add targets for test/prod
# Edit udp.yml to add:
#   targets:
#     dev:
#       workspace:
#         name: udp-dev-workspace
#         capacity_id: "..."
#     test:
#       workspace:
#         name: udp-test-workspace
#         capacity_id: "..."
#     prod:
#       workspace:
#         name: udp-prod-workspace
#         capacity_id: "..."

# Step 4: Commit to git
git init && git add -A
git commit -m "feat: initial export from dev workspace"
git push

# Step 5: CI/CD deploys test and prod automatically

# Step 6: Future changes
# Edit in portal → re-export → commit → CI/CD deploys
udpi30 generate --workspace "udp-dev-workspace"
git add -A && git commit -m "feat: updated ETL logic"
git push
```

### 2.2 Limitations

- `udpi30 generate` exports the current state; manual diff review is needed.
- Portal edits are not automatically captured in git (you must re-export).
- Risk of forgetting to export after portal changes.

### 2.3 When to use

- Teams with existing Fabric workspaces.
- Developers who prefer the portal's notebook experience.
- Projects where you need real Spark and real data during development.

---

## 3. Pattern 3: Git Sync + udpi30

Best for: enterprise teams. Fabric's built-in git sync handles notebook content; udpi30 handles infrastructure and CI/CD deployment.

```
┌─────────────────────────────────────────────────┐
│ Fabric Portal (dev workspace with git sync)     │
│                                                 │
│  1. Enable git sync on dev workspace            │
│     (Settings → Git integration → Connect)      │
│  2. Fabric auto-commits notebook changes to git │
│  3. udpi30 manages everything else:         │
│     - Lakehouses, warehouses, environments      │
│     - Pipelines, schedules, security roles      │
│     - Deployment to test/prod                   │
│                                                 │
│  udp.yml = infrastructure                    │
│  git sync = notebook content                    │
│  udpi30 deploy = promotion to test/prod     │
└─────────────────────────────────────────────────┘
```

### 3.1 How it works

```bash
# Step 1: Set up dev workspace with git sync
# In Fabric portal:
#   Workspace settings → Git integration → Connect to Azure DevOps or GitHub
#   Select repo, branch, folder
#   Fabric syncs notebook content automatically

# Step 2: Create udp.yml for infrastructure
# You can write it manually or generate from the workspace:
udpi30 generate --workspace "udp-dev-workspace"

# Step 3: Edit udp.yml to REMOVE notebook paths
# Since git sync handles notebook content, udpi30 only manages infrastructure:
#
#   notebooks:
#     ingest_to_bronze:
#       # path: ./notebooks/ingest.py  ← REMOVE this, git sync handles it
#       description: "Ingest raw data"
#       environment: spark_env
#       default_lakehouse: bronze
#
# Or keep the paths — udpi30 will update the definition on deploy,
# which is useful if you want CI/CD to override what git sync committed.

# Step 4: Commit udp.yml
git add udp.yml && git commit -m "feat: add infrastructure definition"
git push

# Step 5: Developer workflow
# - Edit notebooks in Fabric portal (auto-synced to git)
# - Edit udp.yml for infrastructure changes (lakehouses, pipelines, security)
# - Both flow through CI/CD:
#   PR → validate → plan → merge → deploy to test → deploy to prod

# Step 6: CI/CD deploys infrastructure to test/prod
# Notebooks are already synced via git
# udpi30 creates lakehouses, pipelines, security, environments
udpi30 deploy --target prod -y
```

### 3.2 Responsibility split: git sync vs. udpi30

| Component | Git Sync | udpi30 |
|-----------|----------|------------|
| Notebook content (.py, .ipynb) | Yes (auto-synced) | Yes (can also deploy) |
| Lakehouse creation | No | Yes |
| Pipeline creation + schedules | No | Yes |
| Environment + libraries | No | Yes |
| Warehouse + SQL views | No | Yes |
| Security roles | No | Yes |
| OneLake data access roles | No | Yes |
| Data agents | No | Yes |
| Multi-environment promotion | No | Yes |
| Drift detection | No | Yes |

### 3.3 Limitations

- Git sync is workspace-level; you cannot sync individual items.
- Git sync only works with Azure DevOps or GitHub.
- Conflict resolution between git sync and `udpi30 deploy` needs care.
- If both git sync and udpi30 update the same notebook, last write wins.

### 3.4 When to use

- Enterprise teams with established git workflows.
- Projects where notebook development happens in the portal.
- Teams that need infrastructure-as-code but do not want to manage notebook files manually.
- When you need security roles, schedules, and environments deployed consistently.

---

## 4. Pattern 4: VS Code + Fabric Extension + udpi30

Best for: developers who want the full VS Code experience (Claude Code, GitHub Copilot, extensions) while running notebooks on real Fabric Spark compute.

The [Fabric Data Engineering VS Code Extension](https://learn.microsoft.com/en-us/fabric/data-engineering/setup-vs-code-extension) lets you author notebooks in VS Code and execute them on remote Fabric Spark without using the portal.

```
┌─────────────────────────────────────────────────┐
│ VS Code                                         │
│                                                 │
│  Fabric Extension  ← connects to workspace      │
│  Claude Code / Copilot  ← AI assistance         │
│  udpi30  ← infrastructure + deployment      │
│                                                 │
│  1. Install Fabric VS Code Extension            │
│  2. Connect to your dev workspace               │
│  3. Create/edit notebooks in VS Code            │
│  4. Run cells on Fabric Spark (remote compute)  │
│  5. udpi30 deploy --target dev  (infra changes)   │
│  6. git commit + push → CI/CD to test/prod      │
└─────────────────────────────────────────────────┘
```

### 4.1 Setup

```bash
# 1. Install the Fabric VS Code extension
#    VS Code → Extensions → search "Fabric Data Engineering" → Install
#    Or: code --install-extension SynapseVSCode.synapse

# 2. Sign in to Fabric
#    Click the Fabric icon in VS Code sidebar → Sign in

# 3. Select your workspace
#    Browse workspaces → select your dev workspace

# 4. Open or create notebooks
#    Notebooks appear in the explorer → edit with full VS Code features
#    Select kernel "Microsoft Fabric Runtime" to run on remote Spark

# 5. Use AI assistance
#    Claude Code: ask it to write Spark code, fix errors, optimize queries
#    Copilot: inline completions while writing PySpark

# 6. udpi30 manages infrastructure
udpi30 init --name udp-project
# Edit udp.yml for lakehouses, pipelines, security
udpi30 deploy --target dev

# 7. Commit everything
git add -A && git commit -m "feat: new ETL pipeline"
git push  # CI/CD deploys to test → prod
```

### 4.2 Responsibility split by tool

| Tool | Responsibility |
|------|----------------|
| Fabric VS Code Extension | Notebook editing + remote Spark execution |
| Claude Code / Copilot | AI-assisted code writing + debugging |
| udpi30 | Infrastructure (lakehouses, pipelines, security, environments) |
| `udpi30 deploy` | Promotion to test/prod |
| Git | Version control + CI/CD trigger |

### 4.3 The AI-assisted development loop

```bash
# In VS Code with Claude Code + Fabric Extension:

# 1. "Create a notebook that reads from bronze and deduplicates by order_id"
#    → Claude Code writes the PySpark code

# 2. Run it on Fabric Spark (Ctrl+Enter on cell)
#    → Executes on remote compute, real data

# 3. "This is slow, optimize the join"
#    → Claude Code rewrites with broadcast join

# 4. Run again → verify it's faster

# 5. "Now deploy the infrastructure"
#    → Claude Code calls udp_deploy via MCP
#    → Or you run: udpi30 deploy --target dev

# 6. Commit and push → CI/CD handles test/prod
```

### 4.4 Limitations

- The Fabric VS Code Extension requires sign-in (it cannot work fully offline).
- Remote Spark execution has startup latency (about 30 seconds for the first cell).
- Some Fabric features (pipeline designer, semantic model editor) are portal-only.
- The extension is GA but some features (AI agent) are still preview.

### 4.5 When to use

- Teams that live in VS Code.
- Developers using Claude Code or GitHub Copilot for AI assistance.
- When you want real Spark execution without leaving your editor.
- When you want local editing, remote compute, AI assistance, and CI/CD in one workflow.

---

## 5. Pattern 5: MCP-Driven Development (Conversational)

Best for: developers using GitHub Copilot or Claude Code who want to manage Fabric entirely through conversation.

With the udpi30 MCP server and the [Microsoft Fabric MCP Server](https://github.com/microsoft/mcp), you can scaffold, deploy, run, and monitor Fabric projects without typing CLI commands.

```
┌─────────────────────────────────────────────────┐
│ GitHub Copilot / Claude Code                     │
│                                                 │
│  MCP Servers:                                   │
│    udpi30-mcp  → deploy, plan, status, run  │
│    udp-mcp      → API docs, item definitions │
│                                                 │
│  Developer talks to AI:                         │
│    "Create a medallion project for sales data"  │
│    "Deploy to dev"                              │
│    "Run the ingest notebook"                    │
│    "Check for drift in prod"                    │
│    "What capacities do I have?"                 │
│    "Show me what's deployed in test"            │
│    "Destroy the dev environment"                │
└─────────────────────────────────────────────────┘
```

### 5.1 Setup

```bash
# 1. Install udpi30 with MCP support
dotnet tool install --global udpi30-mcp

# 2. Authenticate
az login

# 3. Add MCP servers to your IDE

# Claude Code — add to .claude/settings.json:
{
  "mcpServers": {
    "udpi30": {
      "command": "udpi30-mcp"
    }
  }
}

# Optional: also add Microsoft's Fabric MCP for API docs context
# See: https://github.com/microsoft/mcp
```

### 5.2 Example conversation

```
You: "Set up a new Fabric project for our sales analytics"

Claude: I'll create a medallion lakehouse project.
        [calls udp_list_capacities → finds your F8 capacity]
        [calls udp_validate → checks the deployment]
        [writes udp.yml with bronze/silver/gold + ETL notebooks]

You: "Deploy it to dev"

Claude: Let me plan the deployment first.
        [calls udp_plan → shows 12 resources to create]
        Here's what will be created:
          3 lakehouses, 3 notebooks, 2 pipelines, 1 warehouse...
        Should I proceed?

You: "Yes"

Claude: [calls udp_deploy → creates all resources]
        Deployed 12 resources to sales-analytics-dev.

You: "Run the ingest notebook"

Claude: [calls udp_run → submits notebook job]
        Job submitted. Check the Fabric portal for results.

You: "What's the status?"

Claude: [calls udp_status → shows all deployed items]
        12 items deployed, no drift detected.
        Last deploy: 2 minutes ago.

You: "Someone changed something in the portal, check for drift"

Claude: [calls udp_drift → compares deployment vs workspace]
        Drift detected: 1 item added (manual_report) not in udp.yml.

You: "Deploy to prod"

Claude: [calls udp_plan for prod → shows what would change]
        This will create a new workspace 'sales-analytics-prod'
        with 12 resources. Proceed?

You: "Yes, deploy it"

Claude: [calls udp_deploy --target prod]
        Deployed 12 resources to sales-analytics-prod.
```

### 5.3 Two MCP servers, two purposes

| MCP Server | What it does | Install |
|------------|-------------|---------|
| udpi30-mcp | Deploy, plan, status, run, drift, destroy; manages your Fabric project | `dotnet tool install --global udpi30-mcp` |
| [Microsoft Fabric MCP](https://github.com/microsoft/mcp) | Fabric API docs, item definitions, best practices; gives AI context about Fabric | VS Code extension or npm |

Use them together: Microsoft's MCP gives the AI knowledge about Fabric APIs, and the udpi30 MCP gives it the ability to act on your workspace.

### 5.4 Available udpi30 MCP tools

The MCP server exposes 14 tools:

| Tool | What you'd say |
|------|---------------|
| `udp_validate` | "Check if my udp.yml is valid" |
| `udp_plan` | "What would change if I deploy to test?" |
| `udp_deploy` | "Deploy to dev" |
| `udp_destroy` | "Tear down the test environment" |
| `udp_status` | "What's deployed in prod?" |
| `udp_drift` | "Check for drift in staging" |
| `udp_run` | "Run the ETL pipeline in dev" |
| `udp_history` | "Show me recent deployments" |
| `udp_diag` | "Is my Fabric setup working?" |
| `udp_list_templates` | "What templates are available?" |
| `udp_list_workspaces` | "Show me all my workspaces" |
| `udp_list_capacities` | "What capacities do I have?" |
| `udp_export` | "Export the prod workspace items" |
| `udp_generate` | "Generate a udp.yml from my existing workspace" |

### 5.5 Limitations

- MCP tools cannot edit notebook content (use the Fabric VS Code Extension for that).
- Deploy operations require Azure authentication (`az login` or service principal environment variables).
- The udpi30 MCP server is in beta.

### 5.6 When to use

- You already use GitHub Copilot or Claude Code.
- You prefer conversational development over CLI commands.
- Quick operations: "deploy to dev", "check drift", "what's deployed".
- Demos and onboarding; show new team members how Fabric works.

---

## 6. Comparison

| | Write Local | Portal + Export | Git Sync | VS Code + Fabric Ext | MCP-Driven |
|---|---|---|---|---|---|
| Notebook editing | VS Code (no Spark) | Fabric portal | Fabric portal | VS Code (with Spark) | AI writes code |
| Testing | Deploy, then portal | Run in portal | Run in portal | Run from VS Code | "Run the notebook" |
| AI assistance | Claude / Copilot | Fabric Copilot | Fabric Copilot | Claude + Copilot | Full conversation |
| Infrastructure | udp.yml | generated | udp.yml | udp.yml | AI creates it |
| Deploy | `udpi30 deploy` | `udpi30 deploy` | `udpi30 deploy` | `udpi30 deploy` | "Deploy to dev" |
| Best for | Code-first | Existing workspaces | Enterprise | AI + Spark | Conversational |
| Complexity | Low | Medium | Medium-High | Medium | Low |

---

## 7. Recommended starting point

| Situation | Recommended pattern |
|---|---|
| New project | Pattern 1 (Write Local). Run `udpi30 init` and start coding. |
| Existing workspace | Pattern 2 (Portal + Export). Run `udpi30 generate` to capture what you have. |
| Enterprise with git sync | Pattern 3. Use git sync for notebooks, udpi30 for everything else. |
| AI plus real Spark in VS Code | Pattern 4. Install the Fabric VS Code Extension plus Claude Code or Copilot. |
| Conversational operations | Pattern 5. Add the udpi30 MCP server to GitHub Copilot or Claude Code. |
