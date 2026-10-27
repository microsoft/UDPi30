using Udpi30.Ontology.Models;
using Udpi30.Ontology.SemanticModel;

namespace Udpi30.Ontology;

/// <summary>
/// Converts a parsed <see cref="SemanticModelSchema"/> into an
/// <see cref="OntologyDocument"/>: one entity type per table, one property per
/// column, one relationship type per semantic-model relationship — each with an
/// auto-generated logical name and description, and a unique 64-bit ID. Every
/// entity gets a synthetic <c>DisplayName</c> string property that serves as its
/// display name and identifying part, matching the Fabric ontology convention.
/// </summary>
public static class OntologyBuilder
{
    public static OntologyDocument Build(SemanticModelSchema schema, SemanticModelRef source)
    {
        var ids = new IdGenerator();
        var doc = new OntologyDocument
        {
            Name = Naming.ToTechnicalName(source.Name, "Ontology"),
            Namespace = "usertypes",
            SourceModelName = source.Name,
            SourceWorkspaceName = source.WorkspaceName,
            SourceWorkspaceId = source.WorkspaceId,
            SourceModelId = source.ItemId,
        };

        // Map source table name -> built entity, so relationships can resolve ends.
        var byTable = new Dictionary<string, OntologyEntity>(StringComparer.OrdinalIgnoreCase);
        var entityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var globalPropertyTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in schema.Tables)
        {
            var logical = Naming.Humanize(table.Name);
            var entity = new OntologyEntity
            {
                Id = ids.Next(),
                Name = Unique(entityNames, Naming.ToTechnicalName(logical, "Entity")),
                LogicalName = logical,
                SourceTable = table.Name,
            };

            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Synthetic display-name property — the entity's identifying part.
            var displayName = new OntologyProperty
            {
                Id = ids.Next(),
                Name = UniquePropertyName(propertyNames, globalPropertyTypes, "DisplayName", "String"),
                LogicalName = "Display Name",
                ValueType = "String",
                SourceColumn = "",
                Description = $"Display name of the {logical} entity.",
            };
            entity.Properties.Add(displayName);
            entity.DisplayNamePropertyId = displayName.Id;

            foreach (var column in table.Columns)
            {
                var colLogical = Naming.Humanize(column.Name);
                var valueType = MapValueType(column.DataType);
                entity.Properties.Add(new OntologyProperty
                {
                    Id = ids.Next(),
                    Name = UniquePropertyName(
                        propertyNames,
                        globalPropertyTypes,
                        Naming.ToTechnicalName(colLogical, "Property"),
                        valueType),
                    LogicalName = colLogical,
                    ValueType = valueType,
                    SourceColumn = column.Name,
                    Description = Naming.DescribeProperty(colLogical, valueType, column.Name),
                });
            }

            entity.Description = Naming.DescribeEntity(logical, table.Name, entity.Properties.Count - 1);
            doc.Entities.Add(entity);
            byTable[table.Name] = entity;
        }

        var relationshipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in schema.Relationships)
        {
            if (!byTable.TryGetValue(rel.FromTable, out var sourceEntity)
                || !byTable.TryGetValue(rel.ToTable, out var targetEntity))
            {
                // A relationship that references a table we didn't model (e.g. a
                // hidden date table) is skipped rather than producing a dangling end.
                continue;
            }
            if (sourceEntity.Id == targetEntity.Id)
            {
                continue;
            }

            var sourceLogical = sourceEntity.LogicalName;
            var targetLogical = targetEntity.LogicalName;
            var technical = Unique(relationshipNames,
                Naming.ToTechnicalName($"{sourceEntity.Name} Has {targetEntity.Name}", "Relates"));

            doc.Relationships.Add(new OntologyRelationship
            {
                Id = ids.Next(),
                Name = technical,
                LogicalName = $"{sourceLogical} to {targetLogical}",
                Description = Naming.DescribeRelationship(sourceLogical, targetLogical),
                SourceEntityId = sourceEntity.Id,
                TargetEntityId = targetEntity.Id,
            });
        }

        doc.Description = Naming.DescribeOntology(source.Name, doc.Entities.Count, doc.Relationships.Count);
        return doc;
    }

    /// <summary>Map a TMDL column data type to a Fabric ontology value type.</summary>
    public static string MapValueType(string tmdlDataType) => tmdlDataType.Trim().ToLowerInvariant() switch
    {
        "string" => "String",
        "int64" => "BigInt",
        "double" => "Double",
        "decimal" => "Double",
        "datetime" => "DateTime",
        "boolean" => "Boolean",
        "binary" => "Object",
        _ => "String",
    };

    private static string Unique(HashSet<string> seen, string candidate)
    {
        var name = candidate;
        var n = 2;
        while (!seen.Add(name))
        {
            name = $"{candidate}_{n++}";
        }
        return name;
    }

    private static string UniquePropertyName(
        HashSet<string> entityNames,
        Dictionary<string, string> globalTypes,
        string candidate,
        string valueType)
    {
        var baseName = HasTypeConflict(globalTypes, candidate, valueType)
            ? Naming.ToTechnicalName($"{candidate} {valueType}", "Property")
            : candidate;
        var name = baseName;
        var suffix = 2;
        while (entityNames.Contains(name) || HasTypeConflict(globalTypes, name, valueType))
        {
            name = $"{baseName}_{suffix++}";
        }

        entityNames.Add(name);
        globalTypes.TryAdd(name, valueType);
        return name;
    }

    private static bool HasTypeConflict(
        Dictionary<string, string> globalTypes,
        string name,
        string valueType) =>
        globalTypes.TryGetValue(name, out var existingType)
        && !string.Equals(existingType, valueType, StringComparison.Ordinal);
}
