using System.Text.Json;
using Udpi30.Core.Engine;
using Udpi30.Core.Models;
using Udpi30.Core.Providers;

namespace Udpi30.Mcp;

/// <summary>Shared helpers for the MCP tool handlers. Mirrors the module-level helpers in <c>mcp_server/server.py</c>.</summary>
internal static class McpHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Find udp.yml in the given (or current) directory.</summary>
    public static string? FindDeploymentFile(string? projectDir = null)
    {
        var searchDir = string.IsNullOrEmpty(projectDir) ? Directory.GetCurrentDirectory() : projectDir;
        foreach (var name in new[] { "udp.yml", "udp.yaml" })
        {
            var candidate = Path.Combine(searchDir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    public static DeploymentDefinition LoadDeployment(string? projectDir = null, string? target = null)
    {
        var file = FindDeploymentFile(projectDir)
            ?? throw new FileNotFoundException($"No udp.yml found in {projectDir ?? "current directory"}");
        return Loader.LoadDeployment(file, target);
    }

    public static string ProjectDirOf(string? projectDir)
    {
        var file = FindDeploymentFile(projectDir);
        return file is not null ? Path.GetDirectoryName(Path.GetFullPath(file))! : Directory.GetCurrentDirectory();
    }

    public static FabricClient NewClient() => new();

    /// <summary>Format a result object as indented JSON (mirrors _format_result).</summary>
    public static string Format(object? data) =>
        data is string s ? s : JsonSerializer.Serialize(data, JsonOptions);

    public static bool IsGuid(string s) => s.Length == 36 && s.Count(c => c == '-') == 4;
}
