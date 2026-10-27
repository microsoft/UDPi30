using Udpi30.Ontology.SemanticModel;

namespace Udpi30.Ontology.Tests;

public class OntologyBuilderTests
{
    private static readonly SemanticModelRef Source = new("workspace-1", "Workspace", "model-1", "Sales Model");

    [Fact]
    public void Build_SelfRelationship_OmitsInvalidRelationship()
    {
        var schema = new SemanticModelSchema();
        schema.Tables.Add(new SmTable
        {
            Name = "Employee",
            Columns = { new SmColumn { Name = "Id", DataType = "int64" } },
        });
        schema.Relationships.Add(new SmRelationship
        {
            FromTable = "Employee",
            FromColumn = "ManagerId",
            ToTable = "Employee",
            ToColumn = "Id",
        });

        var document = OntologyBuilder.Build(schema, Source);

        Assert.Empty(document.Relationships);
    }

    [Fact]
    public void Build_SamePropertyNameWithDifferentTypes_DisambiguatesTechnicalNames()
    {
        var schema = new SemanticModelSchema();
        schema.Tables.Add(new SmTable
        {
            Name = "Customer",
            Columns = { new SmColumn { Name = "Code", DataType = "string" } },
        });
        schema.Tables.Add(new SmTable
        {
            Name = "Product",
            Columns = { new SmColumn { Name = "Code", DataType = "int64" } },
        });

        var document = OntologyBuilder.Build(schema, Source);
        var customerCode = document.Entities[0].Properties.Single(p => p.SourceColumn == "Code");
        var productCode = document.Entities[1].Properties.Single(p => p.SourceColumn == "Code");

        Assert.NotEqual(customerCode.Name, productCode.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("String", customerCode.ValueType);
        Assert.Equal("BigInt", productCode.ValueType);
        Assert.All(
            document.Entities.SelectMany(entity => entity.AllProperties)
                .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase),
            group => Assert.Single(group.Select(property => property.ValueType).Distinct(StringComparer.Ordinal)));
    }

    [Fact]
    public void Build_GeneratedIds_AreUniquePositiveInt64Values()
    {
        var schema = new SemanticModelSchema();
        schema.Tables.Add(new SmTable
        {
            Name = "Customer",
            Columns =
            {
                new SmColumn { Name = "Id", DataType = "int64" },
                new SmColumn { Name = "Name", DataType = "string" },
            },
        });

        var document = OntologyBuilder.Build(schema, Source);
        var ids = document.Entities.Select(entity => entity.Id)
            .Concat(document.Entities.SelectMany(entity => entity.AllProperties).Select(property => property.Id))
            .Concat(document.Relationships.Select(relationship => relationship.Id))
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.True(long.TryParse(id, out var value) && value > 0));
    }
}
