using System.Collections;
using Udpi30.Core.Models;

namespace Udpi30.Core.Engine;

/// <summary>A node in the dependency graph.</summary>
public sealed class ResourceNode(string key, string resourceType)
{
    public string Key { get; } = key;
    public string ResourceType { get; } = resourceType;
    public HashSet<string> DependsOn { get; } = [];
}

/// <summary>Raised when dependencies cannot be resolved (e.g., cycles).</summary>
public sealed class DependencyResolutionError(string message) : Exception(message);

/// <summary>
/// Computes deployment order via topological sort. Mirrors <c>engine/resolver.py</c>:
/// notebooks depend on their environment/lakehouse, pipelines on referenced
/// notebooks, reports on semantic models, and so on.
/// </summary>
public static class Resolver
{
    /// <summary>
    /// Predefined deployment priority by resource type — used as the secondary
    /// sort within the same dependency level.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> ResourceTypePriority = new Dictionary<string, int>
    {
        ["environments"] = 0,
        ["lakehouses"] = 1,
        ["eventhouses"] = 2,
        ["warehouses"] = 3,
        ["notebooks"] = 4,
        ["semantic_models"] = 5,
        ["reports"] = 6,
        ["pipelines"] = 7,
        ["eventstreams"] = 8,
        ["data_agents"] = 9,
        ["ml_experiments"] = 10,
        ["ml_models"] = 11,
    };

    /// <summary>Build a dependency graph from resource definitions.</summary>
    public static Dictionary<string, ResourceNode> BuildDependencyGraph(ResourcesConfig resources)
    {
        var nodes = new Dictionary<string, ResourceNode>();

        // Register all resources as nodes.
        foreach (var info in ResourceTypeRegistry.All)
        {
            var dict = (IDictionary)typeof(ResourcesConfig).GetProperty(info.PropertyName)!.GetValue(resources)!;
            foreach (var keyObj in dict.Keys)
            {
                var key = (string)keyObj;
                nodes[key] = new ResourceNode(key, info.FieldName);
            }
        }

        void AddDep(string key, string? dep)
        {
            if (!string.IsNullOrEmpty(dep) && nodes.ContainsKey(dep))
            {
                nodes[key].DependsOn.Add(dep);
            }
        }

        // Notebooks depend on environment + default_lakehouse.
        foreach (var (key, nb) in resources.Notebooks)
        {
            AddDep(key, nb.Environment);
            AddDep(key, nb.DefaultLakehouse);
        }

        // Semantic models depend on default lakehouse.
        foreach (var (key, sm) in resources.SemanticModels)
        {
            AddDep(key, sm.DefaultLakehouse);
        }

        // Reports depend on semantic model.
        foreach (var (key, report) in resources.Reports)
        {
            AddDep(key, report.SemanticModel);
        }

        // Pipelines depend on notebooks and other pipelines they reference.
        foreach (var (key, pipeline) in resources.Pipelines)
        {
            foreach (var activity in pipeline.Activities)
            {
                AddDep(key, activity.Notebook);
                AddDep(key, activity.Pipeline);
            }
        }

        // Data agents depend on their sources.
        foreach (var (key, agent) in resources.DataAgents)
        {
            foreach (var src in agent.Sources)
            {
                AddDep(key, src);
            }
        }

        // KQL databases depend on parent eventhouse.
        foreach (var (key, kdb) in resources.KqlDatabases)
        {
            AddDep(key, kdb.ParentEventhouse);
        }

        // KQL dashboards/querysets depend on data source.
        foreach (var (key, res) in resources.KqlDashboards)
        {
            AddDep(key, res.DataSource);
        }
        foreach (var (key, res) in resources.KqlQuerysets)
        {
            AddDep(key, res.DataSource);
        }

        // GraphQL APIs depend on data source.
        foreach (var (key, res) in resources.GraphqlApis)
        {
            AddDep(key, res.DataSource);
        }

        // Spark job definitions depend on environment and lakehouse.
        foreach (var (key, sjd) in resources.SparkJobDefinitions)
        {
            AddDep(key, sjd.Environment);
            AddDep(key, sjd.DefaultLakehouse);
        }

        // Mirrored databases depend on connection.
        foreach (var (key, mdb) in resources.MirroredDatabases)
        {
            AddDep(key, mdb.Connection);
        }

        // dbt jobs depend on environment.
        foreach (var (key, res) in resources.DbtJobs)
        {
            AddDep(key, res.Environment);
        }

        // Digital twin flows depend on twin builder.
        foreach (var (key, res) in resources.DigitalTwinBuilderFlows)
        {
            AddDep(key, res.TwinBuilder);
        }

        // Snowflake/CosmosDB/Databricks depend on connection.
        foreach (var (key, res) in resources.SnowflakeDatabases)
        {
            AddDep(key, res.Connection);
        }
        foreach (var (key, res) in resources.CosmosdbDatabases)
        {
            AddDep(key, res.Connection);
        }
        foreach (var (key, res) in resources.MirroredDatabricksCatalogs)
        {
            AddDep(key, res.Connection);
        }

        // Graph query sets / graph models / paginated reports depend on data source.
        foreach (var (key, res) in resources.GraphQuerySets)
        {
            AddDep(key, res.DataSource);
        }
        foreach (var (key, res) in resources.GraphModels)
        {
            AddDep(key, res.DataSource);
        }
        foreach (var (key, res) in resources.PaginatedReports)
        {
            AddDep(key, res.DataSource);
        }

        return nodes;
    }

    /// <summary>
    /// Topological sort of resource nodes (Kahn's algorithm). Returns nodes in
    /// deployment order (dependencies first); throws on cycles.
    /// </summary>
    public static List<ResourceNode> TopologicalSort(Dictionary<string, ResourceNode> nodes)
    {
        var inDegree = new Dictionary<string, int>();
        foreach (var node in nodes.Values)
        {
            inDegree.TryAdd(node.Key, 0);
            inDegree[node.Key] += node.DependsOn.Count;
        }

        var adj = new Dictionary<string, List<string>>();
        foreach (var node in nodes.Values)
        {
            foreach (var dep in node.DependsOn)
            {
                if (!adj.TryGetValue(dep, out var list))
                {
                    adj[dep] = list = [];
                }
                list.Add(node.Key);
            }
        }

        var queue = new List<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        queue.Sort(StringComparer.Ordinal);
        var result = new List<ResourceNode>();

        while (queue.Count > 0)
        {
            var current = queue[0];
            queue.RemoveAt(0);
            if (nodes.TryGetValue(current, out var node))
            {
                result.Add(node);
            }

            if (adj.TryGetValue(current, out var dependents))
            {
                foreach (var dependent in dependents.OrderBy(s => s, StringComparer.Ordinal))
                {
                    inDegree[dependent] -= 1;
                    if (inDegree[dependent] == 0)
                    {
                        queue.Add(dependent);
                    }
                }
            }
        }

        if (result.Count < nodes.Count)
        {
            var deployed = result.Select(n => n.Key).ToHashSet();
            var remaining = nodes.Keys.Where(k => !deployed.Contains(k)).ToList();
            throw new DependencyResolutionError(
                $"Circular dependency detected involving: [{string.Join(", ", remaining)}]\n" +
                "Check your resource references for cycles.");
        }

        return result;
    }

    /// <summary>Get the full deployment order (dependencies first).</summary>
    public static List<ResourceNode> GetDeploymentOrder(DeploymentDefinition deployment) =>
        TopologicalSort(BuildDependencyGraph(deployment.Resources));

    /// <summary>
    /// Group resources into deployment waves for parallel execution. Each wave
    /// contains resources whose dependencies are satisfied by previous waves.
    /// </summary>
    public static List<List<ResourceNode>> GetDeploymentWaves(DeploymentDefinition deployment)
    {
        var graph = BuildDependencyGraph(deployment.Resources);

        var inDegree = graph.Keys.ToDictionary(k => k, _ => 0);
        var dependents = graph.Keys.ToDictionary(k => k, _ => new List<string>());

        foreach (var (key, node) in graph)
        {
            var validDeps = node.DependsOn.Where(graph.ContainsKey).ToList();
            inDegree[key] = validDeps.Count;
            foreach (var dep in validDeps)
            {
                dependents[dep].Add(key);
            }
        }

        var waves = new List<List<ResourceNode>>();
        var ready = inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();

        while (ready.Count > 0)
        {
            var wave = ready.OrderBy(s => s, StringComparer.Ordinal).Select(k => graph[k]).ToList();
            waves.Add(wave);

            var nextReady = new List<string>();
            foreach (var key in ready)
            {
                foreach (var dependent in dependents[key])
                {
                    inDegree[dependent] -= 1;
                    if (inDegree[dependent] == 0)
                    {
                        nextReady.Add(dependent);
                    }
                }
            }
            ready = nextReady;
        }

        return waves;
    }
}
