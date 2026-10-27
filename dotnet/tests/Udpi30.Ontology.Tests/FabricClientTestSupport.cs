using System.Net;
using System.Text;
using Azure.Core;
using Udpi30.Core.Providers;

namespace Udpi30.Ontology.Tests;

internal sealed class FakeCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new("dummy-token", DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<(string Method, string Url, string? Body)> Requests { get; } = [];

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        Requests.Add((request.Method.Method, request.RequestUri!.ToString(), body));
        return responder(request);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));
}

internal static class FabricClientTestSupport
{
    public static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Accepted(string operationId)
    {
        var response = Json(HttpStatusCode.Accepted, "");
        response.Headers.Location = new Uri($"https://redirect.analysis.windows.net/v1/operations/{operationId}");
        response.Headers.TryAddWithoutValidation("x-ms-operation-id", operationId);
        return response;
    }

    public static FabricClient Build(StubHandler handler) =>
        new(new FabricAuth { Credential = new FakeCredential() }, new HttpClient(handler));
}
