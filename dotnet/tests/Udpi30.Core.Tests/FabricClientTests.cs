using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using Udpi30.Core.Providers;

namespace Udpi30.Core.Tests;

public class FabricClientTests
{
    /// <summary>A TokenCredential that returns a fixed dummy token (no network).</summary>
    private sealed class FakeCredential : TokenCredential
    {
        public int RequestCount { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new($"dummy-token-{++RequestCount}", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Records requests and returns scripted responses.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(string Method, string Url, string? Body)> Requests { get; } = [];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(), body));
            return _responder(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static FabricClient Build(StubHandler handler, FakeCredential? credential = null) =>
        new(new FabricAuth { Credential = credential ?? new FakeCredential() }, new HttpClient(handler));

    [Fact]
    public void ListWorkspaces_ParsesValueArray()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"value":[{"id":"w1","displayName":"Dev"},{"id":"w2","displayName":"Prod"}]}"""));
        var client = Build(handler);

        var workspaces = client.ListWorkspaces();

        Assert.Equal(2, workspaces.Count);
        Assert.Equal("Dev", workspaces[0]["displayName"]!.GetValue<string>());
        Assert.Single(handler.Requests);
        Assert.Equal("GET", handler.Requests[0].Method);
        Assert.EndsWith("/v1/workspaces", handler.Requests[0].Url);
    }

    [Fact]
    public void CreateItem_UsesTypeSpecificEndpoint_AndSendsDefinition()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"item-123"}"""));
        var client = Build(handler);

        var def = System.Text.Json.Nodes.JsonNode.Parse("""{"parts":[{"path":"notebook-content.py","payload":"abc","payloadType":"InlineBase64"}]}""");
        var result = client.CreateItem("ws-1", "ingest", "Notebook", definition: def);

        Assert.Equal("item-123", result["id"]!.GetValue<string>());
        var req = Assert.Single(handler.Requests);
        Assert.Equal("POST", req.Method);
        Assert.EndsWith("/workspaces/ws-1/notebooks", req.Url); // type-specific, not /items
        Assert.Contains("InlineBase64", req.Body);
        Assert.Contains("\"displayName\":\"ingest\"", req.Body);
    }

    [Fact]
    public void CreateItem_FallsBackToGenericItemsEndpoint_ForUnknownType()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"x"}"""));
        var client = Build(handler);

        client.CreateItem("ws-1", "thing", "SomeFutureType");

        var req = Assert.Single(handler.Requests);
        Assert.EndsWith("/workspaces/ws-1/items", req.Url);
        Assert.Contains("\"type\":\"SomeFutureType\"", req.Body);
    }

    [Fact]
    public void AcceptedResponse_UsesOperationIdOnFabricOrigin()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Json(HttpStatusCode.Accepted, "");
            response.Headers.Location = new Uri("https://redirect.analysis.windows.net/v1/operations/op-123");
            response.Headers.TryAddWithoutValidation("x-ms-operation-id", "op-123");
            return response;
        });
        var client = Build(handler);

        var result = client.Request("POST", "/workspaces/ws-1/items", new { displayName = "test" });

        Assert.Equal("op-123", result!["operation_id"]!.GetValue<string>());
        Assert.Equal(
            "https://api.fabric.microsoft.com/v1/operations/op-123",
            result["operation_url"]!.GetValue<string>());
    }

    [Fact]
    public void AcceptedResponse_RejectsCrossOriginLocationWithoutOperationId()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Json(HttpStatusCode.Accepted, "");
            response.Headers.Location = new Uri("https://example.com/v1/operations/op-123");
            return response;
        });
        var client = Build(handler);

        var error = Assert.Throws<FabricApiError>(() =>
            client.Request("POST", "/workspaces/ws-1/items", new { displayName = "test" }));

        Assert.Equal(202, error.StatusCode);
        Assert.Contains("safe long-running operation URL", error.Message);
    }

    [Fact]
    public void WaitForOperationResult_PollsThenRetrievesResult()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/operations/op-123" => Json(HttpStatusCode.OK, """{"status":"Succeeded"}"""),
            "/v1/operations/op-123/result" => Json(HttpStatusCode.OK, """{"id":"item-123"}"""),
            _ => Json(HttpStatusCode.NotFound, """{"message":"unexpected path"}"""),
        });
        var client = Build(handler);

        var result = client.WaitForOperationResult(
            "https://api.fabric.microsoft.com/v1/operations/op-123");

        Assert.Equal("item-123", result!["id"]!.GetValue<string>());
        Assert.Collection(handler.Requests,
            request => Assert.EndsWith("/v1/operations/op-123", request.Url),
            request => Assert.EndsWith("/v1/operations/op-123/result", request.Url));
    }

    [Fact]
    public void WaitForOperation_RateLimitedThenRunning_RetriesUntilSucceeded()
    {
        var attempt = 0;
        var handler = new StubHandler(_ =>
        {
            attempt++;
            var response = attempt switch
            {
                1 => Json(HttpStatusCode.TooManyRequests, """{"message":"slow down"}"""),
                2 => Json(HttpStatusCode.OK, """{"status":"Running"}"""),
                _ => Json(HttpStatusCode.OK, """{"status":"Succeeded"}"""),
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        });
        var client = Build(handler);

        var result = client.WaitForOperation("https://api.fabric.microsoft.com/v1/operations/op-123", timeout: 5);

        Assert.Equal("Succeeded", result!["status"]!.GetValue<string>());
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public void WaitForOperation_UnauthorizedOnce_RefreshesTokenAndRetries()
    {
        var attempt = 0;
        var credential = new FakeCredential();
        var handler = new StubHandler(_ => ++attempt == 1
            ? Json(HttpStatusCode.Unauthorized, """{"message":"expired"}""")
            : Json(HttpStatusCode.OK, """{"status":"Succeeded"}"""));
        var client = Build(handler, credential);

        var result = client.WaitForOperation("https://api.fabric.microsoft.com/v1/operations/op-123", timeout: 5);

        Assert.Equal("Succeeded", result!["status"]!.GetValue<string>());
        Assert.Equal(2, credential.RequestCount);
    }

    [Fact]
    public void WaitForOperation_NonDefaultPort_RejectsBeforeSendingToken()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"status":"Succeeded"}"""));
        var client = Build(handler);

        var error = Assert.Throws<FabricApiError>(() => client.WaitForOperation(
            "https://api.fabric.microsoft.com:444/v1/operations/op-123",
            timeout: 1));

        Assert.Contains("HTTPS endpoint", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Error_Response_RaisesFabricApiError_WithMessage()
    {
        var handler = new StubHandler(_ =>
        {
            var resp = Json(HttpStatusCode.BadRequest, """{"message":"Item name already exists"}""");
            resp.Headers.TryAddWithoutValidation("x-ms-request-id", "req-42");
            return resp;
        });
        var client = Build(handler);

        var ex = Assert.Throws<FabricApiError>(() => client.ListItems("ws-1"));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("Item name already exists", ex.Message);
        Assert.Equal("req-42", ex.RequestId);
    }

    [Fact]
    public void WorkspaceItemsMap_KeyedByDisplayName()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"value":[{"id":"i1","displayName":"bronze","type":"Lakehouse"}]}"""));
        var client = Build(handler);

        var map = client.GetWorkspaceItemsMap("ws-1");

        Assert.True(map.ContainsKey("bronze"));
        Assert.Equal("i1", map["bronze"]["id"]);
        Assert.Equal("Lakehouse", map["bronze"]["type"]);
    }
}
