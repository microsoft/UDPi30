using System.Text.Json.Nodes;
using Udpi30.Core.Models;
using Udpi30.Core.Providers;
using Udpi30.Core.Yaml;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Udpi30.Core.Generators;

/// <summary>
/// One resource discovered in a live environment, ready to be added to a
/// deployment. <see cref="Model"/> is the strongly-typed resource object
/// (e.g. <see cref="EntraGroupResource"/>, <see cref="AzureServiceResource"/>,
/// or a Fabric resource record) keyed under <see cref="FieldName"/> with
/// <see cref="Key"/> — exactly what goes into <see cref="ResourcesConfig"/>.
/// </summary>
public sealed record DiscoveredResource(
    ResourcePlatform Platform,
    string FieldName,
    string Key,
    object Model)
{
    /// <summary>Platform-native type identifier (ARM type, Graph entity, Fabric item type).</summary>
    public string ProviderType =>
        ResourceTypeRegistry.ByField.TryGetValue(FieldName, out var info) ? info.ProviderType : "";
}

/// <summary>
/// Reads what is currently deployed and turns it into typed resource models the
/// reverse generator (and the editor) can drop straight into a deployment. One
/// method per platform: Fabric (workspace items), Entra (Graph directory
/// objects), and Azure (ARM resources via the <c>az</c> CLI).
/// </summary>
public static class ReverseDiscovery
{
    // ----- Fabric -----------------------------------------------------------

    /// <summary>
    /// Discover the items in a Fabric workspace as typed resource models. Only the
    /// item's metadata (description) is captured; item definitions are exported
    /// separately by <c>udpi30 export</c>.
    /// </summary>
    public static List<DiscoveredResource> DiscoverFabric(FabricClient client, string workspaceId)
    {
        var typeMap = ResourceTypeRegistry.ForPlatform(ResourcePlatform.Fabric)
            .ToDictionary(r => r.FabricType, r => r.FieldName);

        var found = new List<DiscoveredResource>();
        foreach (var node in client.ListItems(workspaceId))
        {
            var item = node.AsObject();
            var itemType = item["type"]?.GetValue<string>();
            var name = item["displayName"]?.GetValue<string>();
            if (string.IsNullOrEmpty(itemType) || string.IsNullOrEmpty(name)
                || !typeMap.TryGetValue(itemType, out var field))
            {
                continue;
            }

            var model = NewModel(field);
            if (model is null)
            {
                continue;
            }
            var description = item["description"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(description))
            {
                model.GetType().GetProperty("Description")?.SetValue(model, description);
            }
            found.Add(new DiscoveredResource(ResourcePlatform.Fabric, field, name, model));
        }
        return found;
    }

    // ----- Entra ------------------------------------------------------------

    /// <summary>Discover Entra security groups and app registrations via Microsoft Graph.</summary>
    public static List<DiscoveredResource> DiscoverEntra(GraphClient graph)
    {
        var found = new List<DiscoveredResource>();

        foreach (var node in graph.ListGroups())
        {
            var name = node["displayName"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            found.Add(new DiscoveredResource(ResourcePlatform.Entra, "entra_groups", name,
                new EntraGroupResource
                {
                    Description = node["description"]?.GetValue<string>(),
                    MailNickname = node["mailNickname"]?.GetValue<string>(),
                    SecurityEnabled = node["securityEnabled"]?.GetValue<bool>() ?? true,
                    MailEnabled = node["mailEnabled"]?.GetValue<bool>() ?? false,
                    AssignableToRole = node["isAssignableToRole"]?.GetValue<bool>() ?? false,
                }));
        }

        foreach (var node in graph.ListApplications())
        {
            var name = node["displayName"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            var app = new EntraAppResource
            {
                Description = node["notes"]?.GetValue<string>(),
                SignInAudience = node["signInAudience"]?.GetValue<string>() ?? "AzureADMyOrg",
                // A discovered app already has whatever SP it has; don't force one.
                CreateServicePrincipal = false,
                IdentifierUris = ReadStringList(node["identifierUris"]),
                RedirectUris = ReadStringList(node["web"]?["redirectUris"]),
            };
            found.Add(new DiscoveredResource(ResourcePlatform.Entra, "entra_apps", name, app));
        }

        return found;
    }

    // ----- Azure ------------------------------------------------------------

    // ARM resource type -> the canonical udp.yml field name. Several field names
    // share an ARM type (the storage family, the ML-workspace pair); the first
    // registry row for each type wins, which is the sensible default mapping.
    private static readonly IReadOnlyDictionary<string, string> ArmTypeToField = BuildArmTypeMap();

    private static Dictionary<string, string> BuildArmTypeMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in ResourceTypeRegistry.ForPlatform(ResourcePlatform.Azure))
        {
            map.TryAdd(info.ProviderType, info.FieldName);
        }
        return map;
    }

    /// <summary>
    /// Discover Azure resources via the <c>az</c> CLI. Resource groups come from
    /// <c>az group list</c>; everything else from <c>az resource list</c>, mapped
    /// to its <c>azure_*</c> field by ARM type (unrecognized types are skipped).
    /// Optionally scoped to a single subscription and/or resource group.
    /// </summary>
    public static List<DiscoveredResource> DiscoverAzure(AzureCli az, string? subscription, string? resourceGroup)
    {
        var found = new List<DiscoveredResource>();

        // Resource groups themselves are not returned by `az resource list`.
        if (string.IsNullOrEmpty(resourceGroup) && RunJson(az, AzArgs("group", "list", subscription, null)) is JsonArray groups)
        {
            foreach (var rg in groups.OfType<JsonObject>())
            {
                var name = rg["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                found.Add(new DiscoveredResource(ResourcePlatform.Azure, "azure_resource_groups", name,
                    new AzureResourceGroupResource
                    {
                        Subscription = subscription,
                        Location = rg["location"]?.GetValue<string>(),
                        Tags = ReadTags(rg["tags"]),
                    }));
            }
        }

        if (RunJson(az, AzArgs("resource", "list", subscription, resourceGroup)) is JsonArray resources)
        {
            foreach (var r in resources.OfType<JsonObject>())
            {
                var armType = r["type"]?.GetValue<string>();
                var name = r["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(armType) || string.IsNullOrEmpty(name)
                    || !ArmTypeToField.TryGetValue(armType, out var field))
                {
                    continue;
                }

                var rg = r["resourceGroup"]?.GetValue<string>() ?? "";
                var location = r["location"]?.GetValue<string>();
                var tags = ReadTags(r["tags"]);
                var kind = r["kind"]?.GetValue<string>();
                var sku = r["sku"]?["name"]?.GetValue<string>();

                object model = field == "azure_storage_accounts"
                    ? new AzureStorageAccountResource
                    {
                        Subscription = subscription,
                        ResourceGroup = rg,
                        Location = location,
                        Kind = string.IsNullOrEmpty(kind) ? "StorageV2" : kind,
                        Tags = tags,
                    }
                    : new AzureServiceResource
                    {
                        Subscription = subscription,
                        ResourceGroup = rg,
                        Location = location,
                        Sku = sku,
                        Kind = kind,
                        Tags = tags,
                    };
                found.Add(new DiscoveredResource(ResourcePlatform.Azure, field, name, model));
            }
        }

        return found;
    }

    // ----- YAML projection --------------------------------------------------

    // Mirrors the editor's clean serializer: snake_case keys, no nulls, no empty
    // collections — so a discovered model embeds into the generated udp.yml the
    // same way a hand-authored one would.
    private static readonly ISerializer CleanTyped = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeConverter(new YamlStringEnumConverter())
        .ConfigureDefaultValuesHandling(
            DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    private static readonly IDeserializer Generic = YamlFactory.CreateGenericDeserializer();

    /// <summary>
    /// Project a typed resource model into the plain (snake_case) object graph the
    /// reverse generator serializes, so it merges cleanly with the Fabric output.
    /// </summary>
    public static object? ToYamlObject(object model) =>
        Generic.Deserialize<object?>(CleanTyped.Serialize(model));

    // ----- helpers ----------------------------------------------------------

    private static object? NewModel(string field)
    {
        if (!ResourceTypeRegistry.ByField.TryGetValue(field, out var info))
        {
            return null;
        }
        var prop = typeof(ResourcesConfig).GetProperty(info.PropertyName);
        var valueType = prop?.PropertyType.GetGenericArguments() is [_, var v] ? v : null;
        return valueType is null ? null : Activator.CreateInstance(valueType);
    }

    private static List<string> ReadStringList(JsonNode? node) =>
        node is JsonArray arr
            ? arr.Where(n => n is not null).Select(n => n!.GetValue<string>()).ToList()
            : [];

    private static Dictionary<string, string> ReadTags(JsonNode? tags)
    {
        var result = new Dictionary<string, string>();
        if (tags is JsonObject obj)
        {
            foreach (var kv in obj)
            {
                if (kv.Value is not null)
                {
                    result[kv.Key] = kv.Value.GetValue<string>();
                }
            }
        }
        return result;
    }

    private static List<string> AzArgs(string group, string verb, string? subscription, string? resourceGroup)
    {
        var args = new List<string> { group, verb, "--output", "json" };
        if (!string.IsNullOrEmpty(subscription))
        {
            args.Add("--subscription");
            args.Add(subscription);
        }
        if (!string.IsNullOrEmpty(resourceGroup))
        {
            args.Add("--resource-group");
            args.Add(resourceGroup);
        }
        return args;
    }

    private static JsonNode? RunJson(AzureCli az, IReadOnlyList<string> args)
    {
        var result = az.RunChecked(args);
        return string.IsNullOrWhiteSpace(result.StdOut) ? null : JsonNode.Parse(result.StdOut);
    }
}
