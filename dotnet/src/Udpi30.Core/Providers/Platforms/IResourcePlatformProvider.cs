using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Models;

namespace Udpi30.Core.Providers.Platforms;

/// <summary>
/// State shared with a platform provider for the duration of one deploy run.
/// Carries the resolved deployment, the project directory (for asset paths such
/// as <c>.bicep</c> files), the console, and the dry-run flag. Fabric items keep
/// using <see cref="Deployer"/>'s inline workspace logic; Azure and Entra items
/// — which have no Fabric workspace — are handled by a provider given only this
/// context.
/// </summary>
public sealed class PlatformDeployContext
{
    public required DeploymentDefinition Deployment { get; init; }
    public required string ProjectDir { get; init; }
    public required IAnsiConsole Console { get; init; }
    public required bool DryRun { get; init; }

    /// <summary>
    /// Records a successfully created resource (resource key → provider-native id)
    /// so the orchestrator can roll it back on a later failure. No-op for
    /// resources that don't support deletion-based rollback.
    /// </summary>
    public required Action<string, string> RecordRollback { get; init; }
}

/// <summary>
/// Deploys resources for a single <see cref="ResourcePlatform"/>. One provider
/// owns the client, auth scope, and create/update/delete semantics for its
/// platform. The Fabric path remains inline in <see cref="Deployer"/>; this
/// abstraction is the seam through which Azure (ARM/Bicep) and Entra (Graph)
/// resources are deployed.
/// </summary>
public interface IResourcePlatformProvider
{
    /// <summary>The platform this provider handles.</summary>
    ResourcePlatform Platform { get; }

    /// <summary>
    /// Apply a single plan item. Returns <c>true</c> on success, <c>false</c> on
    /// failure, and <c>null</c> when the item was intentionally skipped — mirroring
    /// the convention used by the Fabric deploy path.
    /// </summary>
    bool? Apply(PlanItem item, PlatformDeployContext ctx);
}
