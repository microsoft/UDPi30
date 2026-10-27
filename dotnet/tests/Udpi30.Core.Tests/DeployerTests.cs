using System.Net;
using System.Text;
using Azure.Core;
using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Providers;

namespace Udpi30.Core.Tests;

public class DeployerTests
{
    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext c, CancellationToken t) => new("t", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext c, CancellationToken t) => ValueTask.FromResult(GetToken(c, t));
    }

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Func<string, string, HttpResponseMessage> _route;
        public List<(string Method, string Url)> Calls { get; } = [];
        public RouteHandler(Func<string, string, HttpResponseMessage> route) => _route = route;
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath));
            return _route(request.Method.Method, request.RequestUri!.AbsolutePath);
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Send(request, ct));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A non-interactive console that discards output (no TTY in test runner).</summary>
    private static IAnsiConsole SilentConsole() => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(TextWriter.Null),
    });

    private static string MakeProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "udp-deploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "notebooks"));
        File.WriteAllText(Path.Combine(dir, "notebooks", "ingest.py"), "print('hello bronze')\n");
        File.WriteAllText(Path.Combine(dir, "udp.yml"), """
            deployment:
              name: test-deploy
              version: "1.0.0"
            workspace:
              workspace_id: "11111111-1111-1111-1111-111111111111"
            resources:
              lakehouses:
                bronze:
                  description: "raw"
              notebooks:
                ingest:
                  path: ./notebooks/ingest.py
                  default_lakehouse: bronze
            """);
        return dir;
    }

    [Fact]
    public void DryRun_Reports_Creates_Without_Network()
    {
        var dir = MakeProject();
        try
        {
            var deployment = Loader.LoadDeployment(Path.Combine(dir, "udp.yml"));
            var plan = Planner.CreatePlan(deployment); // empty workspace => all creates
            // No HTTP calls expected in dry-run with workspace_id set.
            var handler = new RouteHandler((_, _) => throw new InvalidOperationException("no network in dry-run"));
            var client = new FabricClient(new FabricAuth { Credential = new FakeCredential() }, new HttpClient(handler));

            var deployer = new Deployer(client, deployment, dir, SilentConsole(), dryRun: true);
            var result = deployer.Execute(plan);

            Assert.True(result.Success);
            Assert.Equal(2, result.ItemsCreated); // lakehouse + notebook
            Assert.Empty(handler.Calls);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RealDeploy_Creates_Items_And_Builds_Notebook_Definition()
    {
        var dir = MakeProject();
        try
        {
            var deployment = Loader.LoadDeployment(Path.Combine(dir, "udp.yml"));
            var plan = Planner.CreatePlan(deployment);

            var createdBodies = new List<string>();
            var handler = new RouteHandlerCapturing((method, path, body) =>
            {
                createdBodies.Add($"{method} {path} :: {body}");
                if (method == "GET" && path.EndsWith("/items"))
                {
                    return Json("""{"value":[]}"""); // empty workspace
                }
                if (method == "POST" && (path.EndsWith("/lakehouses") || path.EndsWith("/notebooks")))
                {
                    return Json("""{"id":"new-item-id"}""");
                }
                return Json("{}");
            });
            var client = new FabricClient(new FabricAuth { Credential = new FakeCredential() }, new HttpClient(handler));

            var deployer = new Deployer(client, deployment, dir, SilentConsole(), dryRun: false);
            var result = deployer.Execute(plan);

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal(2, result.ItemsCreated);
            // Notebook create must carry an ipynb definition with the inline base64 part.
            var nbCreate = createdBodies.First(b => b.Contains("/notebooks ::"));
            Assert.Contains("artifact.content.ipynb", nbCreate);
            Assert.Contains("InlineBase64", nbCreate);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string MakeTwoLakehouseProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "udp-deploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "udp.yml"), """
            deployment:
              name: test-coe
              version: "1.0.0"
            workspace:
              workspace_id: "11111111-1111-1111-1111-111111111111"
            resources:
              lakehouses:
                good_lh:
                  description: "succeeds"
                bad_lh:
                  description: "fails"
            """);
        return dir;
    }

    [Fact]
    public void ContinueOnError_Keeps_Created_Items_And_Skips_Rollback()
    {
        var dir = MakeTwoLakehouseProject();
        try
        {
            var deployment = Loader.LoadDeployment(Path.Combine(dir, "udp.yml"));
            var plan = Planner.CreatePlan(deployment);

            var handler = new RouteHandlerCapturing((method, path, body) =>
            {
                if (method == "GET" && path.EndsWith("/items")) return Json("""{"value":[]}""");
                if (method == "POST" && path.EndsWith("/lakehouses"))
                {
                    return (body ?? "").Contains("bad_lh")
                        ? Json("""{"message":"DisplayName is Invalid"}""", HttpStatusCode.BadRequest)
                        : Json("""{"id":"good-id"}""");
                }
                return Json("{}");
            });
            var client = new FabricClient(new FabricAuth { Credential = new FakeCredential() }, new HttpClient(handler));

            var deployer = new Deployer(client, deployment, dir, SilentConsole(), dryRun: false) { ContinueOnError = true };
            var result = deployer.Execute(plan);

            Assert.False(result.Success);          // a failure occurred
            Assert.Equal(1, result.ItemsCreated);  // good_lh kept
            Assert.Equal(1, result.ItemsFailed);   // bad_lh failed
            Assert.Empty(result.RollbackLog);      // nothing rolled back
            Assert.DoesNotContain(handler.Methods, m => m == "DELETE");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Default_Rolls_Back_Created_Items_On_Failure()
    {
        var dir = MakeTwoLakehouseProject();
        try
        {
            var deployment = Loader.LoadDeployment(Path.Combine(dir, "udp.yml"));
            var plan = Planner.CreatePlan(deployment);

            var handler = new RouteHandlerCapturing((method, path, body) =>
            {
                if (method == "GET" && path.EndsWith("/items")) return Json("""{"value":[]}""");
                if (method == "POST" && path.EndsWith("/lakehouses"))
                {
                    return (body ?? "").Contains("bad_lh")
                        ? Json("""{"message":"DisplayName is Invalid"}""", HttpStatusCode.BadRequest)
                        : Json("""{"id":"good-id"}""");
                }
                return Json("{}"); // DELETE during rollback
            });
            var client = new FabricClient(new FabricAuth { Credential = new FakeCredential() }, new HttpClient(handler));

            var deployer = new Deployer(client, deployment, dir, SilentConsole(), dryRun: false); // ContinueOnError = false (default)
            var result = deployer.Execute(plan);

            Assert.False(result.Success);
            Assert.NotEmpty(result.RollbackLog);                 // rollback happened
            Assert.Contains(handler.Methods, m => m == "DELETE"); // created item was deleted
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FoldersByType_Creates_Type_Folders_And_Assigns_Items()
    {
        var dir = MakeProject();
        try
        {
            var deployment = Loader.LoadDeployment(Path.Combine(dir, "udp.yml"));
            deployment.Workspace.FoldersByType = true; // opt in to per-type workspace folders (bool?)
            var plan = Planner.CreatePlan(deployment);

            var bodies = new List<string>();
            var folderCreates = new List<string>();
            var handler = new RouteHandlerCapturing((method, path, body) =>
            {
                bodies.Add($"{method} {path} :: {body}");
                if (method == "GET" && path.EndsWith("/items")) return Json("""{"value":[]}""");
                if (method == "GET" && path.EndsWith("/folders")) return Json("""{"value":[]}""");
                if (method == "POST" && path.EndsWith("/folders"))
                {
                    folderCreates.Add(body ?? "");
                    var id = (body ?? "").Contains("Lakehouses") ? "fid-lh" : "fid-nb";
                    return Json($$"""{"id":"{{id}}","displayName":"x"}""");
                }
                if (method == "POST" && (path.EndsWith("/lakehouses") || path.EndsWith("/notebooks")))
                    return Json("""{"id":"new-item-id"}""");
                return Json("{}");
            });
            var client = new FabricClient(new FabricAuth { Credential = new FakeCredential() }, new HttpClient(handler));

            var deployer = new Deployer(client, deployment, dir, SilentConsole(), dryRun: false);
            var result = deployer.Execute(plan);

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal(2, result.ItemsCreated);
            // One folder per type is created (Lakehouses, Notebooks).
            Assert.Equal(2, folderCreates.Count);
            Assert.Contains(folderCreates, b => b.Contains("Lakehouses"));
            Assert.Contains(folderCreates, b => b.Contains("Notebooks"));
            // Each item create carries the folderId of its type folder.
            Assert.Contains(bodies, b => b.Contains("/lakehouses ::") && b.Contains("\"folderId\""));
            Assert.Contains(bodies, b => b.Contains("/notebooks ::") && b.Contains("\"folderId\""));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class RouteHandlerCapturing : HttpMessageHandler
    {
        private readonly Func<string, string, string?, HttpResponseMessage> _route;
        public List<string> Methods { get; } = [];
        public RouteHandlerCapturing(Func<string, string, string?, HttpResponseMessage> route) => _route = route;
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
        {
            Methods.Add(request.Method.Method);
            var body = request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            return _route(request.Method.Method, request.RequestUri!.AbsolutePath, body);
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Send(request, ct));
    }
}
