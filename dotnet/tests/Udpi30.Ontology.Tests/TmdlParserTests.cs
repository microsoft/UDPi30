using Udpi30.Ontology.SemanticModel;

namespace Udpi30.Ontology.Tests;

public class TmdlParserTests
{
    [Fact]
    public void Parse_QuotedNamesAndRelationship_ExtractsSchema()
    {
        const string tmdl = """
            table 'Sales Order'
                column OrderId
                    dataType: int64
                column 'Customer Name'
                    dataType: string

            table Customer
                column Id
                    dataType: int64

            relationship CustomerOrders
                fromColumn: 'Sales Order'.OrderId
                toColumn: Customer.Id
            """;

        var schema = TmdlParser.Parse([tmdl]);

        Assert.Equal(2, schema.Tables.Count);
        Assert.Equal("Sales Order", schema.Tables[0].Name);
        Assert.Equal("Customer Name", schema.Tables[0].Columns[1].Name);
        var relationship = Assert.Single(schema.Relationships);
        Assert.Equal("Sales Order", relationship.FromTable);
        Assert.Equal("OrderId", relationship.FromColumn);
        Assert.Equal("Customer", relationship.ToTable);
        Assert.Equal("Id", relationship.ToColumn);
    }
}
