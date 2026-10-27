using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Udpi30.Core.Providers;
using Udpi30.Ontology.Models;

namespace Udpi30.Ontology;

/// <summary>Outcome of publishing an ontology to a Fabric workspace.</summary>
public sealed record PublishResult(string WorkspaceId, string? ItemId, string DisplayName);

/// <summary>
/// Builds a Fabric Ontology item definition from an <see cref="OntologyDocument"/>
/// and creates it in a workspace via the Fabric REST API. The definition follows
/// the documented ontology format: a <c>.platform</c> part, an empty
/// <c>definition.json</c>, one <c>EntityTypes/{id}/definition.json</c> per entity,
/// and one <c>RelationshipTypes/{id}/definition.json</c> per relationship.
/// </summary>
public static class OntologyPublisher
{
    private const string PlatformSchema =
        "https://developer.microsoft.com/json-schemas/fabric/gitIntegration/platformProperties/2.0.0/schema.json";

    private static readonly Regex TechnicalNamePattern = new(
        "^[A-Za-z][A-Za-z0-9_-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly HashSet<string> AllowedValueTypes = new(StringComparer.Ordinal)
    {
        "String",
        "Boolean",
        "DateTime",
        "Object",
        "BigInt",
        "Double",
    };

    /// <summary>Build the <c>{ "parts": [...] }</c> definition payload for an ontology.</summary>
    public static JsonObject BuildDefinition(OntologyDocument doc)
    {
        ValidateDocument(doc);

        var parts = new JsonArray
        {
            Part(".platform", PlatformPart(doc)),
            Part("definition.json", new JsonObject()),
        };

        foreach (var entity in doc.Entities)
        {
            parts.Add(Part($"EntityTypes/{entity.Id}/definition.json", EntityPart(doc, entity)));
        }

        foreach (var rel in doc.Relationships)
        {
            parts.Add(Part($"RelationshipTypes/{rel.Id}/definition.json", RelationshipPart(doc, rel)));
        }

        return new JsonObject { ["parts"] = parts };
    }

    /// <summary>Create the ontology as a new item in the target workspace.</summary>
    public static PublishResult Publish(FabricClient client, string workspaceId, OntologyDocument doc)
    {
        var definition = BuildDefinition(doc);
        var result = client.CreateItem(workspaceId, doc.Name, "Ontology", definition: definition,
            description: Truncate(doc.Description, 256));

        // A definition-based create may complete asynchronously (HTTP 202).
        var opUrl = result["operation_url"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(opUrl))
        {
            result = client.WaitForOperationResult(opUrl)?.AsObject() ?? result;
        }

        var itemId = result["id"]?.GetValue<string>() ?? result["objectId"]?.GetValue<string>();
        return new PublishResult(workspaceId, itemId, doc.Name);
    }

    // -- part builders -------------------------------------------------------

    private static JsonObject PlatformPart(OntologyDocument doc) => new()
    {
        ["$schema"] = PlatformSchema,
        ["metadata"] = new JsonObject
        {
            ["type"] = "Ontology",
            ["displayName"] = doc.Name,
            ["description"] = Truncate(doc.Description, 256),
        },
        ["config"] = new JsonObject
        {
            ["version"] = "2.0",
            ["logicalId"] = Guid.NewGuid().ToString(),
        },
    };

    private static JsonObject EntityPart(OntologyDocument doc, OntologyEntity entity)
    {
        var properties = new JsonArray();
        foreach (var p in entity.Properties)
        {
            properties.Add(PropertyNode(p));
        }
        var timeseries = new JsonArray();
        foreach (var p in entity.TimeseriesProperties)
        {
            timeseries.Add(PropertyNode(p));
        }

        return new JsonObject
        {
            ["id"] = entity.Id,
            ["namespace"] = doc.Namespace,
            ["baseEntityTypeId"] = null,
            ["name"] = entity.Name,
            ["entityIdParts"] = string.IsNullOrEmpty(entity.DisplayNamePropertyId)
                ? new JsonArray()
                : new JsonArray(entity.DisplayNamePropertyId),
            ["displayNamePropertyId"] = string.IsNullOrEmpty(entity.DisplayNamePropertyId)
                ? null
                : entity.DisplayNamePropertyId,
            ["namespaceType"] = "Custom",
            ["visibility"] = "Visible",
            ["properties"] = properties,
            ["timeseriesProperties"] = timeseries,
        };
    }

    private static JsonObject PropertyNode(OntologyProperty p) => new()
    {
        ["id"] = p.Id,
        ["name"] = p.Name,
        ["redefines"] = null,
        ["baseTypeNamespaceType"] = null,
        ["valueType"] = p.ValueType,
    };

    private static JsonObject RelationshipPart(OntologyDocument doc, OntologyRelationship rel) => new()
    {
        ["namespace"] = doc.Namespace,
        ["id"] = rel.Id,
        ["name"] = rel.Name,
        ["namespaceType"] = "Custom",
        ["source"] = new JsonObject { ["entityTypeId"] = rel.SourceEntityId },
        ["target"] = new JsonObject { ["entityTypeId"] = rel.TargetEntityId },
    };

    private static void ValidateDocument(OntologyDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        if (!string.Equals(doc.Namespace, "usertypes", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ontology namespace must be 'usertypes'.");
        }

        if (doc.Entities is null || doc.Relationships is null)
        {
            throw new InvalidOperationException("Ontology entity and relationship collections cannot be null.");
        }

        var ids = new HashSet<long>();
        var entityIds = new HashSet<string>(StringComparer.Ordinal);
        var entityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var globalPropertyTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in doc.Entities)
        {
            ValidateUniqueId("Entity type", entity.Id, ids);
            ValidateTechnicalName("Entity type", entity.Name);
            if (!entityNames.Add(entity.Name))
            {
                throw new InvalidOperationException($"Duplicate entity type technical name '{entity.Name}'.");
            }
            entityIds.Add(entity.Id);

            if (entity.Properties is null || entity.TimeseriesProperties is null)
            {
                throw new InvalidOperationException(
                    $"Entity type '{entity.Name}' property collections cannot be null.");
            }

            var propertyIds = new HashSet<string>(StringComparer.Ordinal);
            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in entity.AllProperties)
            {
                ValidateUniqueId("Property", property.Id, ids);
                ValidateTechnicalName("Property", property.Name);
                if (!propertyNames.Add(property.Name))
                {
                    throw new InvalidOperationException(
                        $"Entity type '{entity.Name}' has duplicate property technical name '{property.Name}'.");
                }
                propertyIds.Add(property.Id);

                if (!AllowedValueTypes.Contains(property.ValueType))
                {
                    throw new InvalidOperationException(
                        $"Property '{property.Name}' has unsupported value type '{property.ValueType}'.");
                }

                if (globalPropertyTypes.TryGetValue(property.Name, out var existingType)
                    && !string.Equals(existingType, property.ValueType, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Property '{property.Name}' has conflicting value types '{existingType}' and '{property.ValueType}'.");
                }
                globalPropertyTypes.TryAdd(property.Name, property.ValueType);
            }

            if (!string.IsNullOrEmpty(entity.DisplayNamePropertyId))
            {
                ValidateId("Display-name property", entity.DisplayNamePropertyId);
                if (!propertyIds.Contains(entity.DisplayNamePropertyId))
                {
                    throw new InvalidOperationException(
                        $"Entity type '{entity.Name}' references unknown display-name property ID '{entity.DisplayNamePropertyId}'.");
                }
            }
        }

        var relationshipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in doc.Relationships)
        {
            ValidateUniqueId("Relationship type", relationship.Id, ids);
            ValidateTechnicalName("Relationship type", relationship.Name);
            if (!relationshipNames.Add(relationship.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate relationship type technical name '{relationship.Name}'.");
            }
            ValidateId("Relationship source entity", relationship.SourceEntityId);
            ValidateId("Relationship target entity", relationship.TargetEntityId);

            if (!entityIds.Contains(relationship.SourceEntityId))
            {
                throw new InvalidOperationException(
                    $"Relationship '{relationship.Name}' references unknown source entity ID '{relationship.SourceEntityId}'.");
            }
            if (!entityIds.Contains(relationship.TargetEntityId))
            {
                throw new InvalidOperationException(
                    $"Relationship '{relationship.Name}' references unknown target entity ID '{relationship.TargetEntityId}'.");
            }
            if (string.Equals(
                    relationship.SourceEntityId,
                    relationship.TargetEntityId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Relationship '{relationship.Name}' must reference two distinct entity types.");
            }
        }
    }

    private static void ValidateUniqueId(string kind, string id, HashSet<long> ids)
    {
        var value = ValidateId(kind, id);
        if (!ids.Add(value))
        {
            throw new InvalidOperationException($"Duplicate ontology ID '{id}' found on {kind.ToLowerInvariant()}.");
        }
    }

    private static long ValidateId(string kind, string id)
    {
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value <= 0
            || !string.Equals(id, value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{kind} ID '{id}' must be a positive 64-bit integer.");
        }
        return value;
    }

    private static void ValidateTechnicalName(string kind, string name)
    {
        if (!TechnicalNamePattern.IsMatch(name ?? string.Empty))
        {
            throw new InvalidOperationException(
                $"{kind} technical name '{name}' must match ^[A-Za-z][A-Za-z0-9_-]{{0,127}}$.");
        }
    }

    private static JsonObject Part(string path, JsonNode payload) => new()
    {
        ["path"] = path,
        ["payload"] = Base64(payload),
        ["payloadType"] = "InlineBase64",
    };

    private static string Base64(JsonNode node) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(node.ToJsonString(JsonOpts)));

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
