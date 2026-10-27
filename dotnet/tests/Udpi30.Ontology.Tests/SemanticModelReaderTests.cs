using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Udpi30.Ontology.SemanticModel;

namespace Udpi30.Ontology.Tests;

public class SemanticModelReaderTests
{
    [Fact]
    public void ReadSchema_AcceptedOperation_ParsesDefinitionFromOperationResult()
    {
        const string tmdl = """
            table Customer
                column Id
                    dataType: int64
            """;
        var definitionResult = new JsonObject
        {
            ["definition"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["path"] = "definition/tables/Customer.tmdl",
                        ["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(tmdl)),
                        ["payloadType"] = "InlineBase64",
                    },
                },
            },
        };
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/workspaces/workspace-1/semanticModels/model-1/getDefinition" =>
                FabricClientTestSupport.Accepted("op-456"),
            "/v1/operations/op-456" => FabricClientTestSupport.Json(
                HttpStatusCode.OK,
                """{"status":"Succeeded"}"""),
            "/v1/operations/op-456/result" => FabricClientTestSupport.Json(
                HttpStatusCode.OK,
                definitionResult.ToJsonString()),
            _ => FabricClientTestSupport.Json(HttpStatusCode.NotFound, """{"message":"unexpected path"}"""),
        });
        var client = FabricClientTestSupport.Build(handler);

        var schema = SemanticModelReader.ReadSchema(client, "workspace-1", "model-1");

        var table = Assert.Single(schema.Tables);
        Assert.Equal("Customer", table.Name);
        Assert.Equal("Id", Assert.Single(table.Columns).Name);
        Assert.Collection(handler.Requests,
            request => Assert.Contains("format=TMDL", request.Url),
            request => Assert.EndsWith("/v1/operations/op-456", request.Url),
            request => Assert.EndsWith("/v1/operations/op-456/result", request.Url));
    }
}
