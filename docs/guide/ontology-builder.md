# Ontology Builder

The UDPI30 Ontology Builder is a Windows desktop application that converts a
Microsoft Fabric semantic model into an editable Fabric Ontology definition. It
creates technical names, logical names, descriptions, properties, and
relationships, then saves the result locally or publishes it to a Fabric
workspace.

## Install

Download
[`udpi30-ontology-win-x64.zip`](https://github.com/microsoft/UDPi30/releases/latest/download/udpi30-ontology-win-x64.zip)
from the latest GitHub release. Extract the archive on a Windows x64 computer
with the .NET 9 Desktop Runtime and run `Udpi30.Ontology.exe`.

To build from source:

```powershell
cd dotnet
dotnet build src/Udpi30.Ontology -c Release
dotnet run --project src/Udpi30.Ontology
```

The application targets `net9.0-windows` and uses Windows Forms. It is not
published as a NuGet tool because it is an interactive desktop application.

## Authentication

The builder uses the same `FabricAuth` credential chain as the CLI:

1. When `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, and `AZURE_CLIENT_SECRET` are all
   present, it uses that service principal.
2. Otherwise it uses `DefaultAzureCredential`, including an active `az login`
   session or managed identity.
3. Select **Use browser sign-in** in the app to use an interactive browser
   credential.

Use the least-privileged identity that can read the source semantic model and
create an Ontology item in the selected target workspace.

## Build an ontology

1. Select **Source > Load from semantic model** or press `Ctrl+L`.
2. Enter a Fabric workspace name or ID and select **List models**.
3. Select a semantic model and choose **Import**.
4. Review the generated entities, properties, and relationships in the tree.
5. Edit technical names, logical names, descriptions, and value types in the
   property grid.
6. Save the editable source as `*.ontology.json`.
7. Select **Publish > Publish to Fabric** or press `Ctrl+P` to create a new
   Ontology item.

The import reads the semantic model definition in TMDL format. It maps each
table to an entity, each column to a property, and each model relationship to an
ontology relationship. Dimensional prefixes such as `dim_` and `fact_` are
removed when logical names are generated.

Fabric requires a relationship to connect two distinct entity types, so the
builder omits self-referencing semantic-model relationships. Fabric also
requires a property name to use one value type consistently across the entire
ontology. When two tables expose the same property name with different types,
the builder adds the value type to the conflicting technical name, such as
`CodeBigInt`, while retaining the original logical name and source column.

## Generated value types

| Semantic model type | Ontology value type |
| --- | --- |
| `string` | `String` |
| `int64` | `BigInt` |
| `double`, `decimal` | `Double` |
| `dateTime` | `DateTime` |
| `boolean` | `Boolean` |
| `binary` | `Object` |
| Other values | `String` |

Every entity also receives a synthetic `DisplayName` string property. The
builder uses that property as the entity display name and identifying part.

## Local source file

The `*.ontology.json` file is the editable source of truth. It retains generated
descriptions and source metadata that are not represented directly in every
part of the Fabric Ontology schema. You can reopen the file later or pass its
path when starting the app:

```powershell
dotnet run --project src/Udpi30.Ontology -- my-model.ontology.json
```

## Publishing behavior and limits

Publishing creates a new Ontology item with an inline definition containing the
platform metadata, entity type definitions, and relationship type definitions.
Before sending the definition, the builder validates imported and edited source
files against the Fabric contract. It requires canonical positive 64-bit IDs,
unique IDs, the `usertypes` namespace, valid technical names and value types,
valid display-property and relationship references, distinct relationship ends,
and consistent property types. Asynchronous Fabric operations are polled on the
Fabric API endpoint and their final result is retrieved before publication is
reported as successful. The current release does not update an existing item
and does not create data bindings from ontology entities to Lakehouse or
Eventhouse tables. Add those bindings in Fabric after publication.

The Fabric entity schema does not expose a description field for each generated
entity. Detailed descriptions therefore remain in the local source file, while
the overall ontology description is written to the Fabric item metadata.
