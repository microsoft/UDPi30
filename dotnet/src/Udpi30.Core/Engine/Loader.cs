using System.Collections;
using System.Text.RegularExpressions;
using Udpi30.Core.Models;
using Udpi30.Core.Yaml;

namespace Udpi30.Core.Engine;

/// <summary>Raised when a deployment cannot be loaded.</summary>
public sealed class DeploymentLoadError : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public DeploymentLoadError(string message, IReadOnlyList<string>? errors = null) : base(message)
    {
        Errors = errors ?? [];
    }
}

/// <summary>
/// Parses <c>udp.yml</c> (and included files) into a <see cref="DeploymentDefinition"/>.
/// Handles include/extends merging and <c>${var.name}</c> substitution, mirroring
/// <c>engine/loader.py</c>.
/// </summary>
public static partial class Loader
{
    [GeneratedRegex(@"\$\{[^}]+\}")]
    private static partial Regex UnresolvedPattern();

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex VariablePattern();

    [GeneratedRegex(@"^\$\{(?:secret\.[^}]+|keyvault\.[^.}]+\.[^}]+)\}$")]
    private static partial Regex DeferredSecretPattern();

    private static readonly string[] SearchNames =
        ["udp.yml", "udp.yaml", ".udp/deployment.yml", ".udp/deployment.yaml"];

    /// <summary>Find udp.yml by walking up from <paramref name="startDir"/> (or cwd).</summary>
    public static string FindDeploymentFile(string? startDir = null)
    {
        var current = new DirectoryInfo(startDir ?? Directory.GetCurrentDirectory());

        while (current is not null)
        {
            foreach (var name in SearchNames)
            {
                var candidate = Path.Combine(current.FullName, name.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            current = current.Parent;
        }

        throw new DeploymentLoadError(
            $"No udp.yml found in '{startDir ?? Directory.GetCurrentDirectory()}' or any parent directory.\n" +
            "Run 'udpi30 init' to create one, or specify a path with --file.");
    }

    /// <summary>Load and validate a deployment definition from a udp.yml file.</summary>
    public static DeploymentDefinition LoadDeployment(string? path = null, string? target = null, bool strict = false)
    {
        string deploymentPath = path is not null
            ? (File.Exists(path) ? Path.GetFullPath(path)
                : throw new DeploymentLoadError($"Deployment file not found: {path}"))
            : FindDeploymentFile();

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(deploymentPath))!;
        var genericDeserializer = YamlFactory.CreateGenericDeserializer();

        Dictionary<string, object?> data;
        try
        {
            var rawText = File.ReadAllText(deploymentPath);
            var raw = Normalize(genericDeserializer.Deserialize<object?>(rawText));
            if (raw is not Dictionary<string, object?> rawDict)
            {
                throw new DeploymentLoadError($"Empty or invalid deployment file: {deploymentPath}");
            }
            data = ResolveIncludes(rawDict, baseDir);
        }
        catch (DeploymentLoadError)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new DeploymentLoadError($"Invalid YAML in {deploymentPath}: {e.Message}");
        }

        // Deployment inheritance.
        if (data.TryGetValue("extends", out var ext) && ext is string extPath && !string.IsNullOrEmpty(extPath))
        {
            var parentPath = Path.Combine(baseDir, extPath);
            if (File.Exists(parentPath))
            {
                try
                {
                    var parent = Normalize(genericDeserializer.Deserialize<object?>(File.ReadAllText(parentPath)));
                    if (parent is Dictionary<string, object?> parentDict)
                    {
                        parentDict = ResolveIncludes(parentDict, Path.GetDirectoryName(Path.GetFullPath(parentPath))!);
                        data = DeepMerge(parentDict, data);
                    }
                }
                catch (Exception e)
                {
                    throw new DeploymentLoadError($"Error loading parent deployment '{extPath}': {e.Message}");
                }
            }
        }

        // First pass: bind to extract variable definitions.
        var preliminary = Bind(data, deploymentPath, afterSubstitution: false);

        var variables = preliminary.ResolveVariables(target);
        variables["deployment.name"] = preliminary.Deployment.Name;
        variables["deployment.version"] = preliminary.Deployment.Version;
        foreach (DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            variables[$"env.{kv.Key}"] = kv.Value?.ToString() ?? "";
        }

        // Second pass: substitute and re-validate.
        var substituted = (Dictionary<string, object?>)SubstituteVariables(data, variables)!;

        var dumped = YamlFactory.CreateGenericSerializer().Serialize(substituted);
        var unresolved = UnresolvedPattern().Matches(dumped)
            .Select(m => m.Value)
            .Where(value => !DeferredSecretPattern().IsMatch(value))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (unresolved.Count > 0)
        {
            var msg = $"Unresolved variables: {string.Join(", ", unresolved)}";
            if (strict)
            {
                throw new DeploymentLoadError(msg);
            }
            Console.Error.WriteLine($"Warning: {msg}. These will be left as literal strings.");
        }

        return Bind(substituted, deploymentPath, afterSubstitution: true);
    }

    private static DeploymentDefinition Bind(Dictionary<string, object?> data, string path, bool afterSubstitution)
    {
        var yamlText = YamlFactory.CreateGenericSerializer().Serialize(data);
        DeploymentDefinition deployment;
        try
        {
            deployment = YamlFactory.CreateTypedDeserializer().Deserialize<DeploymentDefinition>(yamlText)
                ?? throw new DeploymentLoadError($"Empty or invalid deployment file: {path}");
        }
        catch (DeploymentLoadError)
        {
            throw;
        }
        catch (Exception e)
        {
            var suffix = afterSubstitution ? " after variable substitution" : "";
            throw new DeploymentLoadError(
                $"Deployment validation failed{suffix} ({path}):\n  {e.Message}");
        }

        try
        {
            deployment.ValidateReferences();
        }
        catch (ValidationException e)
        {
            var suffix = afterSubstitution ? " after variable substitution" : "";
            throw new DeploymentLoadError(
                $"Deployment validation failed{suffix} ({path}):\n{e.Message}", e.Errors);
        }

        return deployment;
    }

    /// <summary>Serialize a <see cref="DeploymentDefinition"/> back to YAML.</summary>
    public static string DumpDeployment(DeploymentDefinition deployment) =>
        YamlFactory.CreateTypedSerializer().Serialize(deployment);

    // -- helpers ----------------------------------------------------------

    private static Dictionary<string, object?> ResolveIncludes(Dictionary<string, object?> data, string baseDir)
    {
        if (!data.TryGetValue("include", out var inc) || inc is not List<object?> includes || includes.Count == 0)
        {
            data.Remove("include");
            return data;
        }
        data.Remove("include");

        var merged = new Dictionary<string, object?>();
        var genericDeserializer = YamlFactory.CreateGenericDeserializer();

        foreach (var patternObj in includes)
        {
            var pattern = patternObj?.ToString() ?? "";
            var matched = Glob(baseDir, pattern).ToList();
            if (matched.Count == 0)
            {
                throw new DeploymentLoadError($"Include pattern '{pattern}' matched no files (from {baseDir})");
            }
            foreach (var p in matched)
            {
                var node = Normalize(genericDeserializer.Deserialize<object?>(File.ReadAllText(p)));
                if (node is Dictionary<string, object?> incData)
                {
                    merged = DeepMerge(merged, incData);
                }
            }
        }

        // Base file overrides included files.
        return DeepMerge(merged, data);
    }

    private static IEnumerable<string> Glob(string baseDir, string pattern)
    {
        var full = Path.Combine(baseDir, pattern);
        if (File.Exists(full))
        {
            return [full];
        }

        var normalized = pattern.Replace('/', Path.DirectorySeparatorChar);
        var recursive = normalized.Contains("**");
        normalized = normalized.Replace("**" + Path.DirectorySeparatorChar, "").Replace("**", "");
        var dirPart = Path.GetDirectoryName(normalized) ?? "";
        var filePart = Path.GetFileName(normalized);
        if (string.IsNullOrEmpty(filePart))
        {
            filePart = "*";
        }
        var searchDir = Path.Combine(baseDir, dirPart);
        if (!Directory.Exists(searchDir))
        {
            return [];
        }
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.GetFiles(searchDir, filePart, option).OrderBy(s => s, StringComparer.Ordinal);
    }

    private static Dictionary<string, object?> DeepMerge(Dictionary<string, object?> baseDict, Dictionary<string, object?> overrideDict)
    {
        var result = (Dictionary<string, object?>)DeepCopy(baseDict)!;
        foreach (var (key, value) in overrideDict)
        {
            if (result.TryGetValue(key, out var existing)
                && existing is Dictionary<string, object?> existingDict
                && value is Dictionary<string, object?> valueDict)
            {
                result[key] = DeepMerge(existingDict, valueDict);
            }
            else
            {
                result[key] = DeepCopy(value);
            }
        }
        return result;
    }

    private static object? SubstituteVariables(object? obj, Dictionary<string, string> variables)
    {
        switch (obj)
        {
            case string s:
                return VariablePattern().Replace(s, m =>
                {
                    var expr = m.Groups[1].Value;
                    foreach (var prefix in new[] { "var.", "variables." })
                    {
                        if (expr.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            var varName = expr[prefix.Length..];
                            return variables.TryGetValue(varName, out var v) ? v : m.Value;
                        }
                    }
                    return variables.TryGetValue(expr, out var full) ? full : m.Value;
                });
            case Dictionary<string, object?> d:
                return d.ToDictionary(kv => kv.Key, kv => SubstituteVariables(kv.Value, variables));
            case List<object?> l:
                return l.Select(item => SubstituteVariables(item, variables)).ToList();
            default:
                return obj;
        }
    }

    private static object? DeepCopy(object? node) => node switch
    {
        Dictionary<string, object?> d => d.ToDictionary(kv => kv.Key, kv => DeepCopy(kv.Value)),
        List<object?> l => l.Select(DeepCopy).ToList(),
        _ => node,
    };

    /// <summary>Normalize YamlDotNet's generic output into Dictionary&lt;string,object?&gt;/List&lt;object?&gt;/string.</summary>
    private static object? Normalize(object? node)
    {
        switch (node)
        {
            case IDictionary dict:
                var result = new Dictionary<string, object?>();
                foreach (DictionaryEntry entry in dict)
                {
                    result[entry.Key?.ToString() ?? ""] = Normalize(entry.Value);
                }
                return result;
            case IList list:
                var items = new List<object?>();
                foreach (var item in list)
                {
                    items.Add(Normalize(item));
                }
                return items;
            default:
                return node;
        }
    }
}
