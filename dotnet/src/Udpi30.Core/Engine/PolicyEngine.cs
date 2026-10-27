using System.Collections;
using System.Text.RegularExpressions;
using Udpi30.Core.Models;

namespace Udpi30.Core.Engine;

/// <summary>
/// Policy enforcement — validates a deployment against configurable rules.
/// Mirrors <c>engine/policy.py</c>.
/// </summary>
public static partial class PolicyEngine
{
    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SnakeCasePattern();

    /// <summary>Run all policy checks. Returns the list of violation messages.</summary>
    public static List<string> EnforcePolicies(DeploymentDefinition deployment, string? projectDir = null)
    {
        var violations = new List<string>();
        var policies = deployment.Policies;
        var resources = deployment.Resources;

        if (policies.RequireDescription)
        {
            foreach (var info in ResourceTypeRegistry.All)
            {
                var dict = (IDictionary)typeof(ResourcesConfig).GetProperty(info.PropertyName)!.GetValue(resources)!;
                foreach (DictionaryEntry entry in dict)
                {
                    var descProp = entry.Value?.GetType().GetProperty("Description");
                    var desc = descProp?.GetValue(entry.Value) as string;
                    if (string.IsNullOrEmpty(desc))
                    {
                        violations.Add($"Policy: {entry.Key} ({info.FieldName}) missing description");
                    }
                }
            }
        }

        if (policies.NamingConvention == "snake_case")
        {
            foreach (var key in resources.AllResourceKeys())
            {
                if (!SnakeCasePattern().IsMatch(key))
                {
                    violations.Add($"Policy: '{key}' does not match snake_case convention");
                }
            }
        }

        if (policies.MaxNotebookSizeKb is { } maxKb && projectDir is not null)
        {
            foreach (var (key, nb) in resources.Notebooks)
            {
                var nbPath = Path.Combine(projectDir, nb.Path);
                if (File.Exists(nbPath))
                {
                    var sizeKb = new FileInfo(nbPath).Length / 1024.0;
                    if (sizeKb > maxKb)
                    {
                        violations.Add($"Policy: '{key}' is {sizeKb:F0}KB (max {maxKb}KB)");
                    }
                }
            }
        }

        if (policies.BlockedLibraries.Count > 0)
        {
            foreach (var (key, env) in resources.Environments)
            {
                foreach (var lib in env.Libraries)
                {
                    foreach (var blocked in policies.BlockedLibraries)
                    {
                        var prefix = blocked.Split('<')[0].Split('>')[0].Split('=')[0];
                        if (lib.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            violations.Add($"Policy: '{key}' uses blocked library '{lib}'");
                        }
                    }
                }
            }
        }

        return violations;
    }
}
