using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Engine.State;
using Udpi30.Core.Models;
using Udpi30.Core.Providers;

namespace Udpi30.Cli;

/// <summary>Shared helpers used across CLI commands.</summary>
internal static class CliHelpers
{
    public static IAnsiConsole Ansi => AnsiConsole.Console;

    /// <summary>Project directory: parent of the deployment file if given, else cwd.</summary>
    public static string ProjectDir(string? deploymentFile)
    {
        if (!string.IsNullOrEmpty(deploymentFile) && File.Exists(deploymentFile))
        {
            return Path.GetDirectoryName(Path.GetFullPath(deploymentFile))!;
        }
        return Directory.GetCurrentDirectory();
    }

    /// <summary>Loose GUID check matching the Python CLI (length 36 with four hyphens).</summary>
    public static bool IsGuid(string s) => s.Length == 36 && s.Count(c => c == '-') == 4;

    public static FabricClient NewClient() => new();

    public static StateManager BuildStateManager(DeploymentDefinition deployment, string projectDir, string target)
    {
        var backend = deployment.State;
        return new StateManager(projectDir, target,
            backend.Backend,
            backend.Config.Count > 0 ? backend.Config : null);
    }

    public static bool Confirm(string prompt) =>
        Ansi.Prompt(new ConfirmationPrompt(prompt));

    public static string Ask(string prompt) =>
        Ansi.Prompt(new TextPrompt<string>(prompt));

    public static void Error(string message) =>
        Ansi.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");

    /// <summary>Resolve a workspace id from config (id or name lookup), or null.</summary>
    public static string? ResolveWorkspaceId(FabricClient client, WorkspaceConfig ws)
    {
        if (!string.IsNullOrEmpty(ws.WorkspaceId))
        {
            return ws.WorkspaceId;
        }
        if (!string.IsNullOrEmpty(ws.Name))
        {
            var found = client.FindWorkspace(ws.Name);
            if (found is not null)
            {
                return found["id"]!.GetValue<string>();
            }
        }
        return null;
    }

    public static string FormatTimestamp(double epochSeconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)(epochSeconds * 1000)).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
}
