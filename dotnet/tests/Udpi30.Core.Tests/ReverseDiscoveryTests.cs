using System.Net;
using System.Text;
using Azure.Core;
using Udpi30.Core.Generators;
using Udpi30.Core.Models;
using Udpi30.Core.Providers;

namespace Udpi30.Core.Tests;

public class ReverseDiscoveryTests
{
    // ----- Azure (az CLI) -----

    /// <summary>Returns canned JSON for `az group list` / `az resource list`.</summary>
    private sealed class FakeAz(string groupsJson, string resourcesJson) : AzureCli
    {
        public override AzCliResult Run(IReadOnlyList<string> args)
        {
            var verb = args.Count >= 2 ? $"{args[0]} {args[1]}" : "";
            var json = verb switch
            {
                "group list" => groupsJson,
                "resource list" => resourcesJson,
                _ => "[]",
            };
            return new AzCliResult(0, json, "");
        }
    }

    [Fact]
    public void DiscoverAzure_Maps_Known_Arm_Types_And_Skips_Unknown()
    {
        var groups = """[ { "name": "rg-data", "location": "eastus", "tags": { "env": "prod" } } ]""";
        var resources = """
        [
          { "name": "saanalytics01", "type": "Microsoft.Storage/storageAccounts", "resourceGroup": "rg-data", "location": "eastus", "kind": "StorageV2" },
          { "name": "ehns-events", "type": "Microsoft.EventHub/namespaces", "resourceGroup": "rg-data", "location": "eastus", "sku": { "name": "Standard" } },
          { "name": "mystery", "type": "Microsoft.Unknown/widgets", "resourceGroup": "rg-data", "location": "eastus" }
        ]
        """;
        var found = ReverseDiscovery.DiscoverAzure(new FakeAz(groups, resources), subscription: "sub-1", resourceGroup: null);

        Assert.Contains(found, r => r.FieldName == "azure_resource_groups" && r.Key == "rg-data");
        var sa = Assert.Single(found, r => r.FieldName == "azure_storage_accounts");
        Assert.Equal("saanalytics01", sa.Key);
        Assert.IsType<AzureStorageAccountResource>(sa.Model);
        Assert.Equal("rg-data", ((AzureStorageAccountResource)sa.Model).ResourceGroup);

        var eh = Assert.Single(found, r => r.FieldName == "azure_event_hub_namespaces");
        Assert.Equal("Standard", ((AzureServiceResource)eh.Model).Sku);

        Assert.DoesNotContain(found, r => r.Key == "mystery");
    }

    [Fact]
    public void DiscoverAzure_Scoped_To_ResourceGroup_Skips_Group_Listing()
    {
        // When a resource group is named, `az group list` must not be consulted
        // (no resource_groups row is emitted) — only `az resource list` runs.
        var found = ReverseDiscovery.DiscoverAzure(
            new FakeAz("SHOULD-NOT-BE-USED", "[]"), subscription: null, resourceGroup: "rg-data");
        Assert.DoesNotContain(found, r => r.FieldName == "azure_resource_groups");
    }

    // ----- Entra (Microsoft Graph) -----

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext c, CancellationToken t) => new("t", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext c, CancellationToken t) => ValueTask.FromResult(GetToken(c, t));
    }

    private sealed class RouteHandler(Func<string, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct) =>
            route(request.RequestUri!.AbsolutePath);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Send(request, ct));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public void DiscoverEntra_Reads_Groups_And_Apps()
    {
        var handler = new RouteHandler(path => path switch
        {
            "/v1.0/groups" => Json("""{ "value": [ { "id": "g1", "displayName": "analytics-readers", "description": "readers", "securityEnabled": true, "mailEnabled": false } ] }"""),
            "/v1.0/applications" => Json("""{ "value": [ { "id": "a1", "displayName": "udp-app", "signInAudience": "AzureADMyOrg", "identifierUris": [ "api://udp-app" ] } ] }"""),
            _ => Json("""{ "value": [] }"""),
        });
        var graph = new GraphClient(new FakeCredential(), new HttpClient(handler));

        var found = ReverseDiscovery.DiscoverEntra(graph);

        var group = Assert.Single(found, r => r.FieldName == "entra_groups");
        Assert.Equal("analytics-readers", group.Key);
        Assert.Equal("readers", ((EntraGroupResource)group.Model).Description);

        var app = Assert.Single(found, r => r.FieldName == "entra_apps");
        Assert.Equal("udp-app", app.Key);
        Assert.Contains("api://udp-app", ((EntraAppResource)app.Model).IdentifierUris);
        Assert.False(((EntraAppResource)app.Model).CreateServicePrincipal);
    }

    // ----- YAML projection -----

    [Fact]
    public void ToYamlObject_Produces_SnakeCase_Keys_And_Drops_Empties()
    {
        var model = new EntraGroupResource { Description = "d", Owners = [] };
        var yaml = (System.Collections.IDictionary)ReverseDiscovery.ToYamlObject(model)!;

        Assert.True(yaml.Contains("description"));
        Assert.True(yaml.Contains("security_enabled"));
        Assert.False(yaml.Contains("owners")); // empty list omitted
    }
}
