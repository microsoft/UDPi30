# Contributing to Unified Data Platform Deployment

Thank you for your interest in contributing. This project brings declarative,
Infrastructure-as-Code project management to Microsoft Fabric. It is a .NET 9
solution under [`dotnet/`](dotnet/).

## Development Setup

```bash
git clone https://github.com/microsoft/UDPi30.git
cd udpi30/dotnet
dotnet restore
dotnet build
```

The solution contains:

| Project | Description |
|---|---|
| `src/Udpi30.Core` | Models, engine (loader/resolver/planner/deployer/state), providers, generators. |
| `src/Udpi30.Cli` | The `udpi30` command-line tool (System.CommandLine). |
| `src/Udpi30.Mcp` | The `udpi30-mcp` MCP server. |
| `src/Udpi30.Editor` | Windows Forms editor for `udp.yml`. |
| `src/Udpi30.Ontology` | Windows Forms semantic-model-to-ontology builder. |
| `tests/Udpi30.Core.Tests` | xUnit tests. |

## Running Tests

```bash
dotnet test                                   # Run all tests
dotnet test --logger "console;verbosity=detailed"
```

The repository also has a PowerShell 7 black-box harness for all examples:

```powershell
Invoke-Pester ./deployment-tests/tests
pwsh ./deployment-tests/Invoke-DeploymentTestSuite.ps1 -Mode Validate
```

## Code Quality

```bash
dotnet format                 # Formatting + analyzers
dotnet build -warnaserror     # Treat warnings as errors
```

## Running the tools locally

```bash
# CLI
dotnet run --project src/Udpi30.Cli -- validate -f ../examples/02-medallion-lakehouse/udp.yml

# MCP server (stdio)
dotnet run --project src/Udpi30.Mcp
```

## Adding a New Template

1. Create a directory under `src/Udpi30.Core/Assets/templates/your_template/`.
2. Add a `template.yml` with metadata:
   ```yaml
   name: your-template
   description: "What this template does"
   variables:
     project_name:
       description: "Project name"
       default: "udp-project"
   ```
3. Add a `udp.yml` with `${{variable}}` placeholders.
4. Add supporting files (notebooks, SQL, agent configs). They are shipped as
   content next to the assembly via the `Assets` glob in `Udpi30.Core.csproj`.
5. Add a test in `tests/Udpi30.Core.Tests/TemplateTests.cs`.

## Adding a New Resource Type

1. Add the record in `src/Udpi30.Core/Models/Resources.cs`.
2. Add the property to `ResourcesConfig` (`Models/ResourcesConfig.cs`).
3. Register it in `Models/ResourceTypeRegistry.cs` (field name, Fabric type, naming strictness).
4. Add dependency rules in `Engine/Resolver.cs`.
5. Add deployment/definition logic in `Engine/Deployer.cs` (and the create endpoint in `Providers/FabricClient.cs` if type-specific).
6. Update the JSON Schema in `udp.schema.json` (and the copy under `src/Udpi30.Core/Assets/`).
7. Add tests.

## Pull Request Process

1. Fork the repo and create a branch.
2. Make your changes.
3. Run `dotnet test` and `dotnet format --verify-no-changes`.
4. Submit a PR with a clear description.

## Releasing

Releases publish the `udpi30` and `udpi30-mcp` .NET tool packages to nuget.org
through the [Publish NuGet](.github/workflows/nuget-publish.yml) workflow.

1. Set `<Version>` in `dotnet/Directory.Build.props` and add a dated section to
   `CHANGELOG.md`.
2. Run `dotnet test` and `mkdocs build --clean`, then merge to `main`.
3. Create a GitHub release tagged `vX.Y.Z`. The tag must match the version in
   `Directory.Build.props`. Publishing the release runs the workflow, which builds,
   tests, verifies, and pushes both packages.
4. Run `mkdocs gh-deploy --clean` to publish the documentation.

To validate the packaging without publishing, run the workflow manually
(**Actions → Publish NuGet → Run workflow**) and leave **dry_run** selected. Setting
**dry_run** to false publishes the version in `Directory.Build.props`.

Publishing uses nuget.org Trusted Publishing, so no API key is stored. It requires a
trusted publishing policy for `microsoft/UDPi30` and workflow `nuget-publish.yml`, and
the `NUGET_USER` repository secret.
## Code of Conduct

This project follows the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
