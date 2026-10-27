using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Udpi30.Ontology.Models;

/// <summary>
/// The in-memory (and on-disk) representation of an ontology the tool builds from
/// a Fabric semantic model. This is a richer model than the Fabric ontology item
/// definition: it carries human-friendly <c>LogicalName</c> and <c>Description</c>
/// metadata for every entity, property, and relationship (the tool's value-add).
/// When publishing, the technical <c>Name</c> fields drive the Fabric definition;
/// the descriptions are retained here and summarized into the item description,
/// since the Fabric ontology entity schema has no per-entity description field.
/// </summary>
public sealed class OntologyDocument
{
    [Category("Ontology"), Description("Display name of the ontology item in Fabric.")]
    public string Name { get; set; } = "Ontology";

    [Category("Ontology"), Description("Auto-generated summary description of the ontology.")]
    public string Description { get; set; } = "";

    [Category("Ontology"), Description("Ontology namespace. Fabric requires 'usertypes' for custom types.")]
    public string Namespace { get; set; } = "usertypes";

    [Category("Source"), ReadOnly(true), Description("Name of the source semantic model.")]
    public string SourceModelName { get; set; } = "";

    [Category("Source"), ReadOnly(true), Description("Workspace the source semantic model lives in.")]
    public string SourceWorkspaceName { get; set; } = "";

    [Category("Source"), ReadOnly(true), Description("Workspace ID of the source semantic model.")]
    public string SourceWorkspaceId { get; set; } = "";

    [Category("Source"), ReadOnly(true), Description("Item ID of the source semantic model.")]
    public string SourceModelId { get; set; } = "";

    [Browsable(false)]
    public List<OntologyEntity> Entities { get; set; } = [];

    [Browsable(false)]
    public List<OntologyRelationship> Relationships { get; set; } = [];

    public override string ToString() => Name;
}

/// <summary>One entity type in the ontology (built from one semantic-model table).</summary>
public sealed class OntologyEntity
{
    [Category("Identity"), ReadOnly(true), Description("Unique 64-bit ID of the entity type (assigned by the tool).")]
    public string Id { get; set; } = "";

    [Category("Identity"), Description("Technical name written to Fabric. Must match ^[A-Za-z][A-Za-z0-9_-]{0,127}$.")]
    public string Name { get; set; } = "";

    [Category("Logical"), Description("Human-friendly logical name (may contain spaces). Tool metadata.")]
    public string LogicalName { get; set; } = "";

    [Category("Logical"), Description("Auto-generated description of the entity. Tool metadata.")]
    public string Description { get; set; } = "";

    [Category("Source"), ReadOnly(true), Description("Source semantic-model table this entity was derived from.")]
    public string SourceTable { get; set; } = "";

    [Category("Identity"), ReadOnly(true), Description("ID of the property used as the entity's display name.")]
    public string DisplayNamePropertyId { get; set; } = "";

    [Browsable(false)]
    public List<OntologyProperty> Properties { get; set; } = [];

    [Browsable(false)]
    public List<OntologyProperty> TimeseriesProperties { get; set; } = [];

    [JsonIgnore]
    [Browsable(false)]
    public IEnumerable<OntologyProperty> AllProperties => Properties.Concat(TimeseriesProperties);

    public override string ToString() => string.IsNullOrEmpty(LogicalName) ? Name : $"{LogicalName} ({Name})";
}

/// <summary>One property of an entity type (built from a semantic-model column).</summary>
public sealed class OntologyProperty
{
    [Category("Identity"), ReadOnly(true), Description("Unique 64-bit ID of the property (assigned by the tool).")]
    public string Id { get; set; } = "";

    [Category("Identity"), Description("Technical name written to Fabric. Must match ^[A-Za-z][A-Za-z0-9_-]{0,127}$.")]
    public string Name { get; set; } = "";

    [Category("Logical"), Description("Human-friendly logical name (may contain spaces). Tool metadata.")]
    public string LogicalName { get; set; } = "";

    [Category("Logical"), Description("Auto-generated description of the property. Tool metadata.")]
    public string Description { get; set; } = "";

    [Category("Schema"), Description("Fabric value type: String, BigInt, Double, Boolean, DateTime, or Object.")]
    public string ValueType { get; set; } = "String";

    [Category("Source"), ReadOnly(true), Description("Source semantic-model column this property was derived from.")]
    public string SourceColumn { get; set; } = "";

    public override string ToString() =>
        $"{(string.IsNullOrEmpty(LogicalName) ? Name : LogicalName)} : {ValueType}";
}

/// <summary>
/// One relationship type ("interaction") between two entity types, built from a
/// semantic-model relationship.
/// </summary>
public sealed class OntologyRelationship
{
    [Category("Identity"), ReadOnly(true), Description("Unique 64-bit ID of the relationship type (assigned by the tool).")]
    public string Id { get; set; } = "";

    [Category("Identity"), Description("Technical name written to Fabric. Must match ^[A-Za-z][A-Za-z0-9_-]{0,127}$.")]
    public string Name { get; set; } = "";

    [Category("Logical"), Description("Human-friendly logical name (may contain spaces). Tool metadata.")]
    public string LogicalName { get; set; } = "";

    [Category("Logical"), Description("Auto-generated description of the relationship. Tool metadata.")]
    public string Description { get; set; } = "";

    [Category("Ends"), ReadOnly(true), Description("Entity type ID of the source (many) side.")]
    public string SourceEntityId { get; set; } = "";

    [Category("Ends"), ReadOnly(true), Description("Entity type ID of the target (one) side.")]
    public string TargetEntityId { get; set; } = "";

    public override string ToString() => string.IsNullOrEmpty(LogicalName) ? Name : $"{LogicalName} ({Name})";
}
