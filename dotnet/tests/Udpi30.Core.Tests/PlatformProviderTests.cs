using System.Net;
using System.Text;
using Azure.Core;
using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Models;
using Udpi30.Core.Providers;
using Udpi30.Core.Providers.Platforms;

namespace Udpi30.Core.Tests;

public class PlatformProviderTests
{
    // ----- Registry generalization -----

    [Fact]
    public void ItemTypeMap_Maps_Every_Platform_Field_To_Its_ProviderType()
    {
        Assert.Equal("Lakehouse", ResourceTypeRegistry.ItemTypeMap["lakehouses"]);
        Assert.Equal("group", ResourceTypeRegistry.ItemTypeMap["entra_groups"]);
        Assert.Equal("application", ResourceTypeRegistry.ItemTypeMap["entra_apps"]);
        Assert.Equal("Microsoft.Storage/storageAccounts", ResourceTypeRegistry.ItemTypeMap["azure_storage_accounts"]);
    }

    [Fact]
    public void PlatformFor_Resolves_By_Field_And_Falls_Back_To_Fabric()
    {
        Assert.Equal(ResourcePlatform.Fabric, ResourceTypeRegistry.PlatformFor("lakehouses"));
        Assert.Equal(ResourcePlatform.Entra, ResourceTypeRegistry.PlatformFor("entra_apps"));
        Assert.Equal(ResourcePlatform.Azure, ResourceTypeRegistry.PlatformFor("azure_resource_groups"));
        Assert.Equal(ResourcePlatform.Fabric, ResourceTypeRegistry.PlatformFor("does_not_exist"));
    }

    [Fact]
    public void FabricType_Alias_Equals_ProviderType()
    {
        var info = ResourceTypeRegistry.ByField["notebooks"];
        Assert.Equal(info.ProviderType, info.FabricType);
        Assert.Equal("Notebook", info.FabricType);
    }

    [Fact]
    public void Platform_Partitioning_Has_Expected_Counts()
    {
        Assert.Equal(2, ResourceTypeRegistry.ForPlatform(ResourcePlatform.Entra).Count());
        Assert.Equal(64, ResourceTypeRegistry.ForPlatform(ResourcePlatform.Azure).Count());
        Assert.All(ResourceTypeRegistry.ForPlatform(ResourcePlatform.Fabric),
            r => Assert.Equal(ResourcePlatform.Fabric, r.Platform));
    }

    // ----- Platform-aware name validation -----

    [Fact]
    public void Validation_Flags_Invalid_Storage_Account_Name()
    {
        var rc = new ResourcesConfig();
        rc.AzureStorageAccounts["UPPERnotallowed"] = new() { ResourceGroup = "rg" };
        Assert.Contains(rc.ValidateResourceNames(), w => w.Contains("storage account names"));
    }

    [Fact]
    public void Validation_Allows_Valid_Storage_Account_Name()
    {
        var rc = new ResourcesConfig();
        rc.AzureStorageAccounts["saanalytics01"] = new() { ResourceGroup = "rg" };
        Assert.DoesNotContain(rc.ValidateResourceNames(), w => w.Contains("storage account"));
    }

    [Fact]
    public void Validation_Does_Not_Apply_Fabric_Char_Rules_To_Entra_Names()
    {
        var rc = new ResourcesConfig();
        rc.EntraGroups["O'Brien & Co. Readers"] = new();
        Assert.DoesNotContain(rc.ValidateResourceNames(), w => w.Contains("O'Brien"));
    }

    // ----- Azure provider (Bicep via az CLI) -----

    private sealed class CapturingAz : AzureCli
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public override AzCliResult Run(IReadOnlyList<string> args)
        {
            Calls.Add(args);
            return new AzCliResult(0, "", "");
        }
    }

    private static IAnsiConsole Silent() => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(TextWriter.Null),
    });

    private static PlatformDeployContext Ctx(DeploymentDefinition d, bool dryRun = false) => new()
    {
        Deployment = d,
        ProjectDir = Path.GetTempPath(),
        Console = Silent(),
        DryRun = dryRun,
        RecordRollback = (_, _) => { },
    };

    [Fact]
    public void AzureProvider_ResourceGroup_Issues_SubScope_Deployment()
    {
        var az = new CapturingAz();
        var d = new DeploymentDefinition();
        d.Azure.Subscription = "sub-123";
        d.Resources.AzureResourceGroups["rg-test"] = new() { Location = "eastus" };
        var item = new PlanItem { ResourceKey = "rg-test", ResourceType = "Microsoft.Resources/resourceGroups", Action = PlanAction.Create };

        var result = new AzureResourceProvider(az).Apply(item, Ctx(d));

        Assert.True(result);
        var call = Assert.Single(az.Calls);
        Assert.Equal(new[] { "deployment", "sub", "create" }, call.Take(3));
        Assert.Contains("--location", call);
        Assert.Contains("eastus", call);
        Assert.Contains("--subscription", call);
        Assert.Contains("sub-123", call);
        Assert.Contains("--template-file", call);
    }

    [Fact]
    public void AzureProvider_DryRun_Does_Not_Invoke_Az()
    {
        var az = new CapturingAz();
        var d = new DeploymentDefinition();
        d.Resources.AzureStorageAccounts["sastorage01"] = new() { ResourceGroup = "rg", Location = "eastus" };
        var item = new PlanItem { ResourceKey = "sastorage01", ResourceType = "Microsoft.Storage/storageAccounts", Action = PlanAction.Create };

        var result = new AzureResourceProvider(az).Apply(item, Ctx(d, dryRun: true));

        Assert.True(result);
        Assert.Empty(az.Calls);
    }

    [Fact]
    public void AzureProvider_StorageAccount_Without_ResourceGroup_Is_Skipped()
    {
        var az = new CapturingAz();
        var d = new DeploymentDefinition();
        d.Resources.AzureStorageAccounts["sastorage01"] = new() { Location = "eastus" };
        var item = new PlanItem { ResourceKey = "sastorage01", ResourceType = "Microsoft.Storage/storageAccounts", Action = PlanAction.Create };

        var result = new AzureResourceProvider(az).Apply(item, Ctx(d));

        Assert.Null(result);
        Assert.Empty(az.Calls);
    }

    [Fact]
    public void AzureProvider_GenericService_Issues_GroupScope_Deployment()
    {
        var az = new CapturingAz();
        var d = new DeploymentDefinition();
        d.Azure.Subscription = "sub-123";
        d.Azure.Location = "eastus";
        d.Resources.AzureEventHubNamespaces["ehns-test"] = new() { ResourceGroup = "rg-test", Sku = "Standard" };
        var item = new PlanItem { ResourceKey = "ehns-test", ResourceType = "Microsoft.EventHub/namespaces", Action = PlanAction.Create };

        var result = new AzureResourceProvider(az).Apply(item, Ctx(d));

        Assert.True(result);
        var call = Assert.Single(az.Calls);
        Assert.Equal(new[] { "deployment", "group", "create" }, call.Take(3));
        Assert.Contains("--resource-group", call);
        Assert.Contains("rg-test", call);
        Assert.Contains("--template-file", call);
    }

    [Fact]
    public void AzureProvider_GenericService_Without_ResourceGroup_Is_Skipped()
    {
        var az = new CapturingAz();
        var d = new DeploymentDefinition();
        d.Azure.Location = "eastus";
        d.Resources.AzureCosmosdbAccounts["cosmos-test"] = new();
        var item = new PlanItem { ResourceKey = "cosmos-test", ResourceType = "Microsoft.DocumentDB/databaseAccounts", Action = PlanAction.Create };

        var result = new AzureResourceProvider(az).Apply(item, Ctx(d));

        Assert.Null(result);
        Assert.Empty(az.Calls);
    }

    [Fact]
    public void AllAzureServiceTypes_Have_A_Registered_ApiVersion()
    {
        // Every generic azure_* service row (those NOT handled by a bespoke path)
        // must have an API version, or the generic emitter would skip it.
        var bespoke = new HashSet<string> { "azure_resource_groups", "azure_storage_accounts", "azure_deployments" };
        var d = new DeploymentDefinition();
        var az = new CapturingAz();
        var provider = new AzureResourceProvider(az);
        foreach (var info in ResourceTypeRegistry.ForPlatform(ResourcePlatform.Azure))
        {
            if (bespoke.Contains(info.FieldName))
            {
                continue;
            }
            var prop = typeof(ResourcesConfig).GetProperty(info.PropertyName)!;
            var dict = (System.Collections.IDictionary)prop.GetValue(d.Resources)!;
            dict["probe"] = new AzureServiceResource { ResourceGroup = "rg", Location = "eastus" };
            var item = new PlanItem { ResourceKey = "probe", ResourceType = info.ProviderType, Action = PlanAction.Create };
            // Dry-run: succeeds (true) only if the ARM type resolves to an API version.
            var result = provider.Apply(item, Ctx(d, dryRun: true));
            Assert.True(result, $"{info.FieldName} ({info.ProviderType}) has no registered API version");
            dict.Remove("probe");
        }
    }

    // ----- Entra provider (Microsoft Graph) -----

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext c, CancellationToken t) => new("t", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext c, CancellationToken t) => ValueTask.FromResult(GetToken(c, t));
    }

    private sealed class RouteHandler(Func<string, string, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<(string Method, string Path)> Calls { get; } = [];
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath));
            return route(request.Method.Method, request.RequestUri!.AbsolutePath);
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Send(request, ct));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public void EntraProvider_Creates_Group_When_Absent()
    {
        var handler = new RouteHandler((method, path) => (method, path) switch
        {
            ("GET", "/v1.0/groups") => Json("""{ "value": [] }"""),
            ("POST", "/v1.0/groups") => Json("""{ "id": "grp-1" }""", HttpStatusCode.Created),
            _ => Json("{}", HttpStatusCode.NotFound),
        });
        var graph = new GraphClient(new FakeCredential(), new HttpClient(handler));
        var d = new DeploymentDefinition();
        d.Resources.EntraGroups["analytics-readers"] = new() { Description = "readers" };
        var item = new PlanItem { ResourceKey = "analytics-readers", ResourceType = "group", Action = PlanAction.Create };

        var recorded = new List<string>();
        var ctx = new PlatformDeployContext
        {
            Deployment = d,
            ProjectDir = Path.GetTempPath(),
            Console = Silent(),
            DryRun = false,
            RecordRollback = (key, _) => recorded.Add(key),
        };

        var result = new EntraResourceProvider(graph).Apply(item, ctx);

        Assert.True(result);
        Assert.Contains(("POST", "/v1.0/groups"), handler.Calls);
        Assert.Contains("analytics-readers", recorded);
    }

    [Fact]
    public void EntraProvider_DryRun_Does_Not_Write()
    {
        var handler = new RouteHandler((method, path) => (method, path) switch
        {
            ("GET", "/v1.0/groups") => Json("""{ "value": [] }"""),
            _ => Json("{}", HttpStatusCode.BadRequest),
        });
        var graph = new GraphClient(new FakeCredential(), new HttpClient(handler));
        var d = new DeploymentDefinition();
        d.Resources.EntraGroups["readers"] = new();
        var item = new PlanItem { ResourceKey = "readers", ResourceType = "group", Action = PlanAction.Create };

        var result = new EntraResourceProvider(graph).Apply(item, Ctx(d, dryRun: true));

        Assert.True(result);
        Assert.DoesNotContain(handler.Calls, c => c.Method is "POST" or "PATCH" or "DELETE");
    }
}
