using System.Collections;
using Udpi30.Core.Models;
using Udpi30.Core.Yaml;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Udpi30.Editor;

/// <summary>
/// Loads and saves <c>udp.yml</c> files using the same YamlDotNet configuration
/// the CLI/MCP tooling uses (<see cref="YamlFactory"/>), so the editor reads and
/// writes byte-for-byte compatible YAML.
/// </summary>
/// <remarks>
/// Unlike <c>Loader.LoadDeployment</c>, this does <b>not</b> resolve
/// <c>${var.*}</c> substitutions or merge <c>include</c>/<c>extends</c> — an
/// editor must round-trip the literal source, leaving variable references and
/// includes intact.
/// </remarks>
public static class DeploymentIo
{
    /// <summary>
    /// A "clean" serializer that omits nulls and empty collections, so an
    /// otherwise-empty model (e.g. the 45 unused resource maps, or empty
    /// <c>schemas: []</c> lists) does not pollute the output.
    /// </summary>
    private static ISerializer CleanSerializer() =>
        new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new YamlStringEnumConverter())
            .WithTypeConverter(new YamlVariableValueConverter())
            // Drop computed getter-only properties (e.g. WorkspaceConfig.EffectiveCapacityId)
            // so they don't leak into the file as junk keys.
            .WithTypeInspector(inner => new WritablePropertiesInspector(inner))
            .ConfigureDefaultValuesHandling(
                DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
            .Build();

    /// <summary>Type inspector that only exposes read/write properties, so computed
    /// getter-only properties are never serialized.</summary>
    private sealed class WritablePropertiesInspector(ITypeInspector inner) : ITypeInspector
    {
        public IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container) =>
            inner.GetProperties(type, container).Where(p => p.CanWrite);

        public IPropertyDescriptor GetProperty(
            Type type, object? container, string name, bool ignoreUnmatched, bool caseInsensitivePropertyMatching) =>
            inner.GetProperty(type, container, name, ignoreUnmatched, caseInsensitivePropertyMatching);

        public string GetEnumName(Type enumType, string name) => inner.GetEnumName(enumType, name);

        public string GetEnumValue(object enumValue) => inner.GetEnumValue(enumValue);

        public bool HasParseMethod(Type type) => inner.HasParseMethod(type);

        public object Parse(string value, Type type) => inner.Parse(value, type);
    }

    private const string Header =
        "# udp.yml — Unified Data Platform deployment definition\n" +
        "# Edited with the UDPI30 udp.yml Editor.\n\n";

    /// <summary>Parse a udp.yml file into the typed model (no substitution).</summary>
    public static DeploymentDefinition Load(string path)
    {
        var text = File.ReadAllText(path);
        var def = YamlFactory.CreateTypedDeserializer().Deserialize<DeploymentDefinition>(text);
        return def ?? new DeploymentDefinition();
    }

    /// <summary>Serialize the model to clean udp.yml text.</summary>
    public static string Serialize(DeploymentDefinition def) =>
        Header + CleanSerializer().Serialize(BuildRoot(def));

    /// <summary>Write the model to disk as udp.yml.</summary>
    public static void Save(DeploymentDefinition def, string path) =>
        File.WriteAllText(path, Serialize(def));

    /// <summary>
    /// Run cross-reference + naming validation. Returns an empty list when valid,
    /// otherwise the list of human-readable error messages.
    /// </summary>
    public static List<string> Validate(DeploymentDefinition def)
    {
        try
        {
            def.ValidateReferences();
            return [];
        }
        catch (Udpi30.Core.ValidationException ex)
        {
            var errors = new List<string>();
            if (ex.Errors is { Count: > 0 })
            {
                errors.AddRange(ex.Errors);
            }
            else
            {
                errors.Add(ex.Message);
            }
            return errors;
        }
        catch (Exception ex)
        {
            return [ex.Message];
        }
    }

    // -- output assembly -----------------------------------------------------

    /// <summary>
    /// Build an ordered top-level map containing only the sections that have
    /// content. Values are the strongly-typed model objects themselves, so the
    /// clean serializer applies the naming convention and converters recursively.
    /// </summary>
    private static Dictionary<string, object?> BuildRoot(DeploymentDefinition def)
    {
        var root = new Dictionary<string, object?> { ["deployment"] = def.Deployment };

        if (WorkspaceHasContent(def.Workspace))
        {
            root["workspace"] = def.Workspace;
        }
        if (def.Include.Count > 0)
        {
            root["include"] = def.Include;
        }
        if (!string.IsNullOrEmpty(def.Extends))
        {
            root["extends"] = def.Extends;
        }
        if (def.Variables.Count > 0)
        {
            root["variables"] = def.Variables;
        }

        var resources = BuildResourcesMap(def.Resources);
        if (resources.Count > 0)
        {
            root["resources"] = resources;
        }

        if (AzureHasContent(def.Azure))
        {
            root["azure"] = def.Azure;
        }

        if (def.Security.Roles.Count > 0)
        {
            root["security"] = def.Security;
        }
        if (def.Connections.Count > 0)
        {
            root["connections"] = def.Connections;
        }
        if (PoliciesHaveContent(def.Policies))
        {
            root["policies"] = def.Policies;
        }
        if (def.Notifications.OnSuccess.Count > 0 || def.Notifications.OnFailure.Count > 0)
        {
            root["notifications"] = def.Notifications;
        }
        if (StateHasContent(def.State))
        {
            root["state"] = def.State;
        }
        if (def.Admin.TenantSettings.Count > 0)
        {
            root["admin"] = def.Admin;
        }
        if (def.Targets.Count > 0)
        {
            root["targets"] = def.Targets;
        }

        return root;
    }

    /// <summary>Resource-type maps that contain at least one entry, in registry order.</summary>
    private static Dictionary<string, object?> BuildResourcesMap(ResourcesConfig resources)
    {
        var map = new Dictionary<string, object?>();
        foreach (var info in ResourceTypeRegistry.All)
        {
            var prop = typeof(ResourcesConfig).GetProperty(info.PropertyName);
            if (prop?.GetValue(resources) is IDictionary dict && dict.Count > 0)
            {
                map[info.FieldName] = dict;
            }
        }
        return map;
    }

    private static bool AzureHasContent(AzureConfig a) =>
        !string.IsNullOrEmpty(a.Subscription) || !string.IsNullOrEmpty(a.Location);

    private static bool WorkspaceHasContent(WorkspaceConfig w) =>
        !string.IsNullOrEmpty(w.Name)
        || !string.IsNullOrEmpty(w.WorkspaceId)
        || !string.IsNullOrEmpty(w.CapacityId)
        || !string.IsNullOrEmpty(w.Capacity)
        || !string.IsNullOrEmpty(w.Description)
        || w.GitIntegration is not null;

    private static bool PoliciesHaveContent(PolicyConfig p) =>
        p.Rules.Count > 0
        || p.RequireDescription
        || !string.IsNullOrEmpty(p.NamingConvention)
        || p.MaxNotebookSizeKb is not null
        || p.BlockedLibraries.Count > 0;

    private static bool StateHasContent(StateConfig s) =>
        (!string.IsNullOrEmpty(s.Backend) && s.Backend != "local") || s.Config.Count > 0;
}
