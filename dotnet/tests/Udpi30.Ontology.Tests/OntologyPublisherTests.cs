using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Udpi30.Ontology.Models;

namespace Udpi30.Ontology.Tests;

public class OntologyPublisherTests
{
    [Fact]
    public void BuildDefinition_ValidDocument_EmitsRequiredPartTree()
    {
        var document = CreateDocument();

        var definition = OntologyPublisher.BuildDefinition(document);

        var parts = definition["parts"]!.AsArray().Select(node => node!.AsObject()).ToList();
        Assert.Contains(parts, part => part["path"]!.GetValue<string>() == ".platform");
        Assert.Contains(parts, part => part["path"]!.GetValue<string>() == "definition.json");
        Assert.Contains(parts, part => part["path"]!.GetValue<string>() == "EntityTypes/101/definition.json");
        Assert.Contains(parts, part => part["path"]!.GetValue<string>() == "EntityTypes/201/definition.json");
        Assert.Contains(parts, part => part["path"]!.GetValue<string>() == "RelationshipTypes/301/definition.json");
        Assert.All(parts, part => Assert.Equal("InlineBase64", part["payloadType"]!.GetValue<string>()));

        var platform = DecodePart(parts.Single(part => part["path"]!.GetValue<string>() == ".platform"));
        Assert.Equal("Ontology", platform["metadata"]!["type"]!.GetValue<string>());
        Assert.Equal(document.Name, platform["metadata"]!["displayName"]!.GetValue<string>());
    }

    [Fact]
    public void BuildDefinition_SelfRelationship_ThrowsBeforeCallingFabric()
    {
        var document = CreateDocument();
        document.Relationships[0].TargetEntityId = document.Relationships[0].SourceEntityId;

        var error = Assert.Throws<InvalidOperationException>(() => OntologyPublisher.BuildDefinition(document));

        Assert.Contains("distinct", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDefinition_ConflictingGlobalPropertyTypes_ThrowsBeforeCallingFabric()
    {
        var document = CreateDocument();
        document.Entities[1].Properties.Add(new OntologyProperty
        {
            Id = "203",
            Name = "Code",
            LogicalName = "Code",
            ValueType = "BigInt",
            SourceColumn = "Code",
        });

        var error = Assert.Throws<InvalidOperationException>(() => OntologyPublisher.BuildDefinition(document));

        Assert.Contains("conflicting value types", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("path-shaped-id", "positive 64-bit integer")]
    [InlineData("duplicate-id", "duplicate ontology ID")]
    [InlineData("invalid-namespace", "namespace")]
    [InlineData("invalid-name", "technical name")]
    [InlineData("invalid-value-type", "unsupported value type")]
    [InlineData("unknown-display-property", "unknown display-name property")]
    [InlineData("unknown-relationship-source", "unknown source entity")]
    [InlineData("duplicate-entity-name", "duplicate entity type technical name")]
    [InlineData("duplicate-property-name", "duplicate property technical name")]
    [InlineData("duplicate-relationship-name", "duplicate relationship type technical name")]
    public void BuildDefinition_InvalidImportedDocument_ThrowsBeforeCallingFabric(
        string scenario,
        string expectedMessage)
    {
        var document = CreateDocument();
        switch (scenario)
        {
            case "path-shaped-id":
                document.Entities[0].Id = "../../definition.json";
                break;
            case "duplicate-id":
                document.Relationships[0].Id = document.Entities[0].Id;
                break;
            case "invalid-namespace":
                document.Namespace = "system";
                break;
            case "invalid-name":
                document.Entities[0].Name = "Customer/../../Admin";
                break;
            case "invalid-value-type":
                document.Entities[0].Properties[0].ValueType = "Decimal";
                break;
            case "unknown-display-property":
                document.Entities[0].DisplayNamePropertyId = "999";
                break;
            case "unknown-relationship-source":
                document.Relationships[0].SourceEntityId = "999";
                break;
            case "duplicate-entity-name":
                document.Entities[1].Name = document.Entities[0].Name.ToLowerInvariant();
                break;
            case "duplicate-property-name":
                document.Entities[0].Properties[1].Name = document.Entities[0].Properties[0].Name.ToLowerInvariant();
                break;
            case "duplicate-relationship-name":
                document.Relationships.Add(new OntologyRelationship
                {
                    Id = "302",
                    Name = document.Relationships[0].Name.ToLowerInvariant(),
                    SourceEntityId = document.Entities[1].Id,
                    TargetEntityId = document.Entities[0].Id,
                });
                break;
            default:
                throw new InvalidOperationException($"Unknown test scenario '{scenario}'.");
        }

        var error = Assert.Throws<InvalidOperationException>(() => OntologyPublisher.BuildDefinition(document));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Publish_AcceptedOperation_ReturnsCreatedItemFromOperationResult()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/workspaces/workspace-1/ontologies" => FabricClientTestSupport.Accepted("op-123"),
            "/v1/operations/op-123" => FabricClientTestSupport.Json(
                HttpStatusCode.OK,
                """{"status":"Succeeded"}"""),
            "/v1/operations/op-123/result" => FabricClientTestSupport.Json(
                HttpStatusCode.OK,
                """{"id":"ontology-123","displayName":"SalesOntology","type":"Ontology"}"""),
            _ => FabricClientTestSupport.Json(HttpStatusCode.NotFound, """{"message":"unexpected path"}"""),
        });
        var client = FabricClientTestSupport.Build(handler);

        var result = OntologyPublisher.Publish(client, "workspace-1", CreateDocument());

        Assert.Equal("ontology-123", result.ItemId);
        Assert.Collection(handler.Requests,
            request =>
            {
                Assert.Equal("POST", request.Method);
                Assert.EndsWith("/v1/workspaces/workspace-1/ontologies", request.Url);
                Assert.Contains("\"definition\"", request.Body);
            },
            request => Assert.EndsWith("/v1/operations/op-123", request.Url),
            request => Assert.EndsWith("/v1/operations/op-123/result", request.Url));
    }

    private static OntologyDocument CreateDocument()
    {
        var customer = new OntologyEntity
        {
            Id = "101",
            Name = "Customer",
            LogicalName = "Customer",
            DisplayNamePropertyId = "102",
            Properties =
            {
                new OntologyProperty
                {
                    Id = "102",
                    Name = "DisplayName",
                    LogicalName = "Display Name",
                    ValueType = "String",
                },
                new OntologyProperty
                {
                    Id = "103",
                    Name = "Code",
                    LogicalName = "Code",
                    ValueType = "String",
                    SourceColumn = "Code",
                },
            },
        };
        var order = new OntologyEntity
        {
            Id = "201",
            Name = "Order",
            LogicalName = "Order",
            DisplayNamePropertyId = "202",
            Properties =
            {
                new OntologyProperty
                {
                    Id = "202",
                    Name = "DisplayName",
                    LogicalName = "Display Name",
                    ValueType = "String",
                },
            },
        };
        return new OntologyDocument
        {
            Name = "SalesOntology",
            Namespace = "usertypes",
            Entities = { customer, order },
            Relationships =
            {
                new OntologyRelationship
                {
                    Id = "301",
                    Name = "CustomerHasOrder",
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                },
            },
        };
    }

    private static JsonObject DecodePart(JsonObject part)
    {
        var payload = part["payload"]!.GetValue<string>();
        return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)))!.AsObject();
    }
}
