# ${{project_name}}

A Microsoft Fabric project managed by [Unified Data Platform Deployment](https://github.com/microsoft/UDPi30).

## Project Structure

```
${{project_name}}/
├── udp.yml              # Deployment definition — all resources, targets, security
├── notebooks/                    # Notebooks and source code
│   └── sample_notebook.py  # Sample PySpark notebook
├── resources/              # Pipeline configs, SQL scripts, agent instructions
├── tests/                  # Validation tests
│   └── test_validate.py    # Checks udp.yml is valid
└── README.md               # This file
```

## Getting Started

### Prerequisites

- Python 3.10+
- Azure CLI (`az login`)
- A Microsoft Fabric capacity

### Setup

```bash
# Install udpi30
dotnet tool install --global udpi30

# Find your capacity GUID
az rest --method get \
  --url "https://api.fabric.microsoft.com/v1/capacities" \
  --resource "https://api.fabric.microsoft.com"

# Update udp.yml with your capacity_id
```

### Deploy

```bash
udpi30 validate          # Check for errors
udpi30 plan -t dev       # Preview changes
udpi30 deploy -t dev     # Deploy to dev workspace
```

### Develop

Edit notebooks in `notebooks/`, then redeploy:

```bash
udpi30 deploy -t dev
```

Or use the [Fabric VS Code Extension](https://learn.microsoft.com/en-us/udpric/data-engineering/setup-vs-code-extension) to edit and run notebooks on remote Spark compute.

### CI/CD

See the [CI/CD guide](https://microsoft.github.io/UDPi30/cicd/overview/) for GitHub Actions and Azure DevOps templates.

## Commands

| Command | Description |
|---------|-------------|
| `udpi30 validate` | Validate udp.yml |
| `udpi30 plan -t dev` | Preview changes |
| `udpi30 deploy -t dev` | Deploy to dev |
| `udpi30 destroy -t dev` | Tear down dev |
| `udpi30 status -t dev` | Show deployed resources |
| `udpi30 drift -t dev` | Detect portal changes |
| `udpi30 run <notebook>` | Run a notebook |
