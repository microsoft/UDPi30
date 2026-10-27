# udp.yml Editor (Windows)

The **udp.yml Editor** is a standalone Windows desktop application for creating
and updating [`udp.yml`](udp-yml.md) deployment definitions through a graphical
tree + property-grid interface — no hand-editing of YAML required.

It is built on the same `Udpi30.Core` model and serializer that power the
`udpi30` CLI and MCP server, so:

- it understands **every resource type across all three platforms** automatically
  — the 46 Fabric item types, Microsoft **Entra** groups/apps, and the 64 **Azure**
  service types;
- the files it writes are byte-compatible with `udpi30 deploy`/`validate`;
- it runs the **same validation** as `udpi30 validate`.

!!! note "Windows only"
    The editor targets `net9.0-windows` and uses Windows Forms. The CLI and MCP
    server remain cross-platform; only this GUI is Windows-specific.

## Installing and running

The editor ships in the repository as the `Udpi30.Editor` project.

```powershell
# from the dotnet/ folder of the repo
dotnet run --project src/Udpi30.Editor

# open an existing file directly
dotnet run --project src/Udpi30.Editor -- ..\examples\02-medallion-lakehouse\udp.yml
```

To produce a runnable build:

```powershell
dotnet build src/Udpi30.Editor -c Release
# → src/Udpi30.Editor/bin/Release/net9.0-windows/Udpi30.Editor.exe
```

You can also launch the `.exe` directly and pass a `udp.yml` path as the first
argument.

## The window

```
┌──────────────────────────────┬───────────────────────────────────────┐
│ Deployment: medallion         │  Name            medallion-analytics   │
│ Workspace                     │  Version         1.0.0                  │
│ Azure (defaults)              │  Description      Bronze/Silver/Gold…   │
│ Variables (2)                 │  Folders By Type  True                  │
│ Resources                     │                                         │
│   notebooks (3)               │   ← property grid edits the item        │
│   lakehouses (3)              │     selected on the left                │
│   Entra (1)                   │                                         │
│     entra_groups (1)          │                                         │
│   Azure (3)                   │                                         │
│     azure_key_vaults (1)      │                                         │
│ Security roles (2)            │                                         │
│ Targets (3)                   │                                         │
│ Advanced                      │                                         │
└──────────────────────────────┴───────────────────────────────────────┘
```

- **Left — tree.** Every section of the deployment: `Deployment`, `Workspace`,
  `Azure (defaults)` (the `azure:` subscription/location defaults), `Variables`,
  `Resources`, `Security`, `Connections`, `Targets`, `Admin / tenant settings`,
  and an `Advanced` group (`Policies`, `Notifications`, `State`). Under
  `Resources`, **Fabric** types sit at the top level; **Entra** and **Azure**
  types are grouped under their platform so 100+ types stay navigable.
- **Right — property grid.** Edits the object selected in the tree: scalars,
  enums (drop-downs), lists (collection editor), string maps (key/value dialog),
  and nested objects (inline or via a dialog that can create a missing section).

## Editing

| Task | How |
|---|---|
| Edit a field | Select a node, change the value in the property grid |
| Add a resource | **Edit ▸ Add Resource…** (`Ctrl+R`); use the **platform filter** (Fabric / Entra / Azure) to narrow the type list, then pick a type and key |
| Set Azure defaults | Select **Azure (defaults)**, set `subscription` / `location` for `azure_*` resources |
| Add a variable / connection / target / tenant setting | **Edit ▸ Add …** |
| Rename a key | Select the node and press **F2** (or right-click ▸ Rename) |
| Remove an item | Select the node and press **Delete** |
| Edit a list (e.g. shortcuts, security roles) | Click the **…** collection editor on the property |
| Edit a key/value map (e.g. spark properties, parameters) | Click the **…** map editor on the property |

## Tools

- **Tools ▸ Import from deployed environment…** (`Ctrl+I`) — connects to the live
  **Fabric** workspace, **Microsoft Entra**, and/or **Azure** subscription, lists
  what is currently deployed, and lets you pick which resources to reverse-generate
  into the open `udp.yml`. Check the sources to scan (and an Azure subscription /
  resource group, if any), click **Discover**, filter the list, check the
  resources you want, then **Import Selected** adds them — skipping any key already
  present. It runs the same discovery engine as `udpi30 generate --include-entra
  --include-azure`, with the same credentials (`AZURE_*` env vars /
  `DefaultAzureCredential`; Azure also needs `az login`).
- **Tools ▸ Validate** (`F5`) — runs the same cross-reference and naming checks
  as `udpi30 validate` and lists any issues.
- **Tools ▸ View YAML** — previews exactly what will be written to disk.
- **Tools ▸ Group items into workspace folders by type** — sets
  `workspace.folders_by_type` so `deploy` organizes items into Fabric workspace
  folders by type. See [Workspace folders by type](#workspace-folders-by-type).

## Workspace folders by type

Toggling **Group items into workspace folders by type** sets
`workspace.folders_by_type: true`. On deploy, each item is created inside a
Fabric workspace folder named after its type category. A per-item `folder:`
value always overrides the type folder. See the
[`folders_by_type` reference](udp-yml.md#44-workspace-folders-by-type) for the
full type → folder mapping.

## How it maps to `udp.yml`

The editor round-trips the **literal** source:

- It does **not** resolve `${var.*}` references or merge `include` / `extends` —
  those stay verbatim, as an editor should preserve them.
- Output omits nulls and empty collections, so unused resource maps never
  clutter the file; meaningful defaults (e.g. `enable_schemas: true`) are kept.

Because it uses the production model, anything the editor saves can be deployed
directly:

```powershell
udpi30 validate
udpi30 plan -t dev
udpi30 deploy -t dev
```

## Limitations

- Windows only (Windows Forms).
- Loading a file that contains a value the model cannot represent (for example
  an unknown `connections.*.type`) reports a precise error rather than silently
  dropping it — fix the value and reopen.
- Folder assignment applies when items are **created**; existing items are not
  moved into folders on update.
