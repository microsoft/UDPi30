using Udpi30.Core.Engine;
using Udpi30.Core.Models;

namespace Udpi30.Core.Tests;

public class LoaderTests
{
    /// <summary>Locate the repo root by walking up to the folder containing 'examples'.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "examples")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repo root (examples/).");
    }

    private static string ExamplePath(string rel) =>
        Path.Combine(RepoRoot(), "examples", rel.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Loads_Medallion_Example()
    {
        var path = ExamplePath("02-medallion-lakehouse/udp.yml");
        var deployment = Loader.LoadDeployment(path, target: "dev");

        Assert.Equal("medallion-analytics", deployment.Deployment.Name);
        Assert.Equal("1.0.0", deployment.Deployment.Version);

        // Resources bound correctly.
        Assert.Equal(3, deployment.Resources.Lakehouses.Count);
        Assert.True(deployment.Resources.Lakehouses["bronze"].EnableSchemas);
        Assert.Equal(3, deployment.Resources.Notebooks.Count);
        Assert.Equal("spark_env", deployment.Resources.Notebooks["ingest_to_bronze"].Environment);
        Assert.Equal("bronze", deployment.Resources.Notebooks["ingest_to_bronze"].DefaultLakehouse);

        // Pipeline activities + depends_on.
        var pipeline = deployment.Resources.Pipelines["daily_etl"];
        Assert.Equal(3, pipeline.Activities.Count);
        Assert.Equal("0 6 * * *", pipeline.Schedule!.Cron);
        Assert.Contains("ingest", pipeline.Activities[1].DependsOn);

        // Security roles + enum mapping (contributor / read).
        Assert.Equal(2, deployment.Security.Roles.Count);
        Assert.Equal(WorkspaceRole.Contributor, deployment.Security.Roles[0].WorkspaceRole);
        Assert.Equal(WorkspaceRole.Viewer, deployment.Security.Roles[1].WorkspaceRole);
        Assert.Contains(OneLakePermission.Read, deployment.Security.Roles[1].OnelakeRoles[0].Permissions);

        // Variable substitution: dev target injects capacity_id default into workspace.
        var ws = deployment.GetEffectiveWorkspace("dev");
        Assert.Equal("medallion-dev", ws.Name);
        Assert.Equal("REPLACE-WITH-YOUR-CAPACITY-GUID", ws.CapacityId);
    }

    [Fact]
    public void Loads_Minimal_Example()
    {
        var path = ExamplePath("01-minimal/udp.yml");
        var deployment = Loader.LoadDeployment(path);
        Assert.False(string.IsNullOrEmpty(deployment.Deployment.Name));
    }

    [Fact]
    public void Loads_Shortcuts_And_Connections_Example()
    {
        var path = ExamplePath("06-shortcuts-and-connections/udp.yml");
        var deployment = Loader.LoadDeployment(path, target: "dev", strict: true);
        var connection = deployment.Connections["adls_source"];

        Assert.Equal(ConnectionType.AdlsGen2, connection.Type);
        Assert.Equal("service_principal", connection.AuthMethod);
        Assert.Equal(3, deployment.Resources.Lakehouses["data_hub"].Shortcuts.Count);
    }

    [Fact]
    public void Strict_Load_Allows_Deferred_Secret_References()
    {
        var source = File.ReadAllText(ExamplePath("05-multi-environment/udp.yml"));
        var yaml = source.Replace(
            "${secret.UDP_TEST_SLACK_WEBHOOK}",
            "${keyvault.contoso-kv.slack-webhook}",
            StringComparison.Ordinal);
        var path = Path.Combine(Path.GetTempPath(), $"udp-loader-{Guid.NewGuid():N}.yml");

        try
        {
            File.WriteAllText(path, yaml);
            var deployment = Loader.LoadDeployment(path, target: "dev", strict: true);
            Assert.Equal("enterprise-analytics", deployment.Deployment.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Strict_Load_Rejects_Unresolved_Ordinary_Variable()
    {
        const string yaml = """
            deployment:
              name: strict-loader-test
              version: "1.0.0"
            resources:
              lakehouses:
                test_lakehouse:
                  description: "Loader test"
            targets:
              dev:
                workspace:
                  name: strict-loader-test-dev
                  capacity_id: "${var.missing}"
            """;
        var path = Path.Combine(Path.GetTempPath(), $"udp-loader-{Guid.NewGuid():N}.yml");

        try
        {
            File.WriteAllText(path, yaml);
            var error = Assert.Throws<DeploymentLoadError>(() =>
                Loader.LoadDeployment(path, target: "dev", strict: true));
            Assert.Contains("${var.missing}", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
