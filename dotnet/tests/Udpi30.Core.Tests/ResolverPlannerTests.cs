using Udpi30.Core.Engine;
using Udpi30.Core.Models;

namespace Udpi30.Core.Tests;

public class ResolverPlannerTests
{
    private static DeploymentDefinition Medallion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "examples")))
        {
            dir = dir.Parent;
        }
        var path = Path.Combine(dir!.FullName, "examples", "02-medallion-lakehouse", "udp.yml");
        return Loader.LoadDeployment(path, target: "dev");
    }

    [Fact]
    public void Topological_Order_Respects_Dependencies()
    {
        var deployment = Medallion();
        var order = Resolver.GetDeploymentOrder(deployment).Select(n => n.Key).ToList();

        int Idx(string k) => order.IndexOf(k);

        // Environment + lakehouses come before the notebooks that depend on them.
        Assert.True(Idx("spark_env") < Idx("ingest_to_bronze"));
        Assert.True(Idx("bronze") < Idx("ingest_to_bronze"));
        Assert.True(Idx("silver") < Idx("bronze_to_silver"));

        // Pipeline depends on its notebooks, so it comes after them.
        Assert.True(Idx("ingest_to_bronze") < Idx("daily_etl"));
        Assert.True(Idx("bronze_to_silver") < Idx("daily_etl"));
        Assert.True(Idx("silver_to_gold") < Idx("daily_etl"));

        // data agent depends on gold + analytics_warehouse.
        Assert.True(Idx("gold") < Idx("analytics_agent"));
        Assert.True(Idx("analytics_warehouse") < Idx("analytics_agent"));
    }

    [Fact]
    public void Plan_All_Create_When_Workspace_Empty()
    {
        var deployment = Medallion();
        var plan = Planner.CreatePlan(deployment, "dev");

        Assert.True(plan.HasChanges);
        Assert.Empty(plan.Errors);
        // Every resource should be a CREATE since the (empty) workspace has nothing.
        Assert.All(plan.Items, i => Assert.Equal(PlanAction.Create, i.Action));
        Assert.Equal(deployment.Resources.AllResourceKeys().Count, plan.Creates.Count);
        // Fabric item-type mapping applied (notebooks -> Notebook, pipelines -> DataPipeline).
        Assert.Contains(plan.Items, i => i is { ResourceKey: "daily_etl", ResourceType: "DataPipeline" });
        Assert.Contains(plan.Items, i => i is { ResourceKey: "spark_env", ResourceType: "Environment" });
    }

    [Fact]
    public void Cycle_Detection_Throws()
    {
        var nodes = new Dictionary<string, ResourceNode>();
        var a = new ResourceNode("a", "notebooks");
        var b = new ResourceNode("b", "notebooks");
        a.DependsOn.Add("b");
        b.DependsOn.Add("a");
        nodes["a"] = a;
        nodes["b"] = b;

        Assert.Throws<DependencyResolutionError>(() => Resolver.TopologicalSort(nodes));
    }
}
