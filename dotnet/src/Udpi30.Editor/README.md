# UDPI30 `udp.yml` Editor

A standalone Windows Forms application for **creating and updating `udp.yml`
deployment definitions** with a tree + property-grid UI — no YAML hand-editing
required.

It references `Udpi30.Core` and reuses the project's own model graph and
YamlDotNet configuration, so the files it writes are byte-compatible with the
`udpi30` CLI and MCP server, and it automatically supports **all resource types
across every platform** — the 46 Fabric item types, Microsoft **Entra** groups
and app registrations, and the 64 **Azure** service types.

## Running

```powershell
# from the dotnet/ folder
dotnet run --project src/Udpi30.Editor

# or open a file directly
dotnet run --project src/Udpi30.Editor -- ..\examples\02-medallion-lakehouse\udp.yml
```

A published single build:

```powershell
dotnet build src/Udpi30.Editor -c Release
# -> src/Udpi30.Editor/bin/Release/net9.0-windows/Udpi30.Editor.exe
```

> Windows-only: this project targets `net9.0-windows` and uses Windows Forms.

## What you can edit

- **Deployment** metadata, **Workspace** (incl. Git integration), **Azure
  defaults** (subscription/location for `azure_*` resources), **Variables**
  (literal or description/default form).
- **Resources** — every supported type across all three platforms. Fabric items
  sit directly under **Resources**; **Entra** and **Azure** types are grouped
  under their platform. Add via **Edit ▸ Add Resource**, which has a **platform
  filter** (Fabric / Entra / Azure) so the 100+ types stay navigable.
- **Security** roles + OneLake role bindings, **Connections**, **Targets**
  (per-environment workspace/variables/run-as/strategy), **Admin** tenant
  settings, and the advanced **Policies / Notifications / State** sections.

The right-hand property grid edits scalars, enums, lists (collection editor),
string maps (key/value dialog), and nested objects (inline or via a dialog that
can create a missing section). Rename a key with **F2** / right-click; remove
with **Delete**.

## Tools

- **Tools ▸ Import from deployed environment…** (`Ctrl+I`) connects to the live
  **Fabric** workspace, **Microsoft Entra**, and/or **Azure** subscription, lists
  what is currently deployed, and lets you pick which resources to reverse-generate
  into the open `udp.yml`. Filter the list, check the resources you want, and
  **Import Selected** adds them (skipping keys already present). It uses the same
  discovery engine as `udpi30 generate`, with the same credentials (`AZURE_*`
  env vars / `az login` / `DefaultAzureCredential`).
- **Tools ▸ Validate** (`F5`) runs the same cross-reference and naming checks as
  `udpi30 validate`.
- **Tools ▸ View YAML** previews exactly what will be written.
- **Tools ▸ Group items into workspace folders by type** sets
  `workspace.folders_by_type`, so `deploy` organizes items into Fabric workspace
  folders by type (Notebooks, Pipelines, Lakehouses, Reports, Models, Databases,
  Warehouses, Environments, Agents, Variables). A per-item `folder` overrides the
  type folder.

## Notes on output

- The editor **does not** resolve `${var.*}` references or merge `include` /
  `extends` — it round-trips the literal source, as an editor should.
- Output omits nulls and empty collections, so unused resource maps don't clutter
  the file; meaningful defaults (e.g. `enable_schemas: true`) are kept.
