# UDPI30 Ontology Builder

A standalone Windows Forms application that **reads a Microsoft Fabric semantic
model and turns it into a digital-twin-builder ontology** — entity types,
properties, and relationship types — with **logical names and descriptions
generated automatically for every entity, attribute, and interaction**. The
ontology can be saved to a local file and published as a Fabric **Ontology** item.

It references `Udpi30.Core` and reuses the project's `FabricClient` /
`FabricAuth`, so it authenticates and talks to Fabric exactly like the rest of the
toolset.

## Running

```powershell
# from the dotnet/ folder
dotnet run --project src/Udpi30.Ontology

# or open an existing ontology file directly
dotnet run --project src/Udpi30.Ontology -- my-model.ontology.json
```

A release build:

```powershell
dotnet build src/Udpi30.Ontology -c Release
# -> src/Udpi30.Ontology/bin/Release/net9.0-windows/Udpi30.Ontology.exe
```

> Windows-only: this project targets `net9.0-windows` and uses Windows Forms.

## Workflow

1. **Source ▸ Load from semantic model…** (`Ctrl+L`) — enter a workspace (name or
   ID), list its semantic models, and pick one. The tool fetches the model's TMDL
   definition (`getDefinition?format=TMDL`), parses its tables, columns, and
   relationships, and builds an ontology:
   - one **entity type** per table (with a synthetic `DisplayName` property used as
     the entity's display name and identifying part),
   - one **property** per column, with the Fabric value type inferred from the
     column data type (`int64`→`BigInt`, `double`/`decimal`→`Double`,
     `dateTime`→`DateTime`, `boolean`→`Boolean`, otherwise `String`),
   - one **relationship type** per semantic-model relationship (source = the
     many side, target = the one side); self-referencing relationships are
     omitted because Fabric requires two distinct entity types.
   Dimensional prefixes (`dim_`, `fact_`, …) are stripped and identifiers are
   humanized: `dim_Customer` → **Customer**, `FactSalesOrder` → **Sales Order**,
   `unit_price_usd` → **Unit Price Usd**.
2. **Edit** — the left tree shows the ontology (entities → properties, and
   relationships); the right property grid edits the technical **Name**, the
   human-friendly **Logical Name**, the **Description**, and a property's
   **Value Type**. **Source ▸ Regenerate names & descriptions** re-derives all
   logical names and descriptions from the source identifiers.
3. **File ▸ Save** (`Ctrl+S`) — writes a local `*.ontology.json` file: the editable
   source of truth that round-trips logical names and descriptions alongside the
   technical structure. Re-open it any time with **File ▸ Open**.
4. **Publish ▸ Publish to Fabric…** (`Ctrl+P`) — creates a new **Ontology** item in
   a target workspace. The tool builds the documented ontology item definition (a
   `.platform` part, an empty `definition.json`, one `EntityTypes/{id}/definition.json`
   per entity, and one `RelationshipTypes/{id}/definition.json` per relationship),
   base64-encodes each part, and calls *Create Item with definition*.

## Authentication

Uses the same credentials as the rest of UDPI30: service-principal `AZURE_*`
environment variables when present, otherwise `DefaultAzureCredential`
(`az login` / managed identity). Tick **Use browser sign-in** in the load/publish
dialogs to force an interactive `InteractiveBrowserCredential`.

## Notes

- The Fabric ontology **entity** schema has no per-entity description field, so
  descriptions live in the local `.ontology.json` (the tool's documentation layer)
  and the generated ontology summary is written to the Fabric item description.
- The published ontology defines the **structure** (entity/relationship types). Data
  bindings (mapping entities to lakehouse/eventhouse tables) are left to be added in
  digital twin builder, where you connect each entity to its source data.
- Property names use one value type consistently across the ontology. If two
  source tables use the same column name with different types, the conflicting
  technical property name receives a type suffix such as `CodeBigInt`.
- Publish follows the Fabric long-running-operation status and result endpoints
  and validates edited files before it sends the definition.
