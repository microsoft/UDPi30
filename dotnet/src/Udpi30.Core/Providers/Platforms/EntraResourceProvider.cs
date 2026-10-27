using System.Text.Json.Nodes;
using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Models;

namespace Udpi30.Core.Providers.Platforms;

/// <summary>
/// Deploys Microsoft Entra directory objects (security groups and application
/// registrations) through Microsoft Graph. The resource key is the object's
/// <c>displayName</c>; create is idempotent (an existing object by display name
/// is patched instead of duplicated).
/// </summary>
public sealed class EntraResourceProvider : IResourcePlatformProvider
{
    private readonly GraphClient _graph;

    public EntraResourceProvider(GraphClient? graph = null) => _graph = graph ?? new GraphClient();

    public ResourcePlatform Platform => ResourcePlatform.Entra;

    public bool? Apply(PlanItem item, PlatformDeployContext ctx) => item.ResourceType switch
    {
        "group" => ApplyGroup(item, ctx),
        "application" => ApplyApp(item, ctx),
        _ => Skip(ctx, item, $"unsupported Entra type '{item.ResourceType}'"),
    };

    private static bool? Skip(PlatformDeployContext ctx, PlanItem item, string why)
    {
        ctx.Console.MarkupLine($"  [yellow]![/] {Markup.Escape(item.ResourceKey)}: {Markup.Escape(why)} — skipping");
        return null;
    }

    // ----- Groups -----

    private bool? ApplyGroup(PlanItem item, PlatformDeployContext ctx)
    {
        var name = item.ResourceKey;
        ctx.Deployment.Resources.EntraGroups.TryGetValue(name, out var group);
        group ??= new EntraGroupResource();

        if (item.Action == PlanAction.Delete)
        {
            return DeleteByName(ctx, name, _graph.ResolveGroup(name), id => _graph.DeleteGroup(id), "group");
        }

        var existingId = _graph.ResolveGroup(name);
        if (ctx.DryRun)
        {
            return WouldApply(ctx, item, existingId is not null, "group");
        }

        var body = new JsonObject
        {
            ["displayName"] = name,
            ["mailEnabled"] = group.MailEnabled,
            ["mailNickname"] = group.MailNickname ?? Sanitize(name),
            ["securityEnabled"] = group.SecurityEnabled,
        };
        if (!string.IsNullOrEmpty(group.Description))
        {
            body["description"] = group.Description;
        }
        if (group.AssignableToRole)
        {
            body["isAssignableToRole"] = true;
        }

        if (existingId is not null)
        {
            _graph.UpdateGroup(existingId, body);
            ctx.Console.MarkupLine($"  [yellow]~[/] Updated Entra group: {Markup.Escape(name)}");
            SyncMembers(ctx, existingId, group);
            return true;
        }

        // Owners are set at creation via @odata.bind; members are added afterward
        // by $ref so the operation stays idempotent on re-runs.
        AttachBind(body, "owners", group.Owners.Select(o => _graph.ResolvePrincipal(o, "User")).Where(g => g is not null)!);

        var newId = _graph.CreateGroup(body);
        if (string.IsNullOrEmpty(newId))
        {
            return false;
        }
        ctx.RecordRollback(name, newId);
        ctx.Console.MarkupLine($"  [green]+[/] Created Entra group: {Markup.Escape(name)}");
        SyncMembers(ctx, newId, group);
        return true;
    }

    private void SyncMembers(PlatformDeployContext ctx, string groupId, EntraGroupResource group)
    {
        foreach (var member in group.Members)
        {
            var id = _graph.ResolvePrincipal(member, "User");
            if (id is null)
            {
                ctx.Console.MarkupLine($"    [dim]· member '{Markup.Escape(member)}' not resolved — skipped[/]");
                continue;
            }
            _graph.AddGroupMember(groupId, id);
        }
    }

    // ----- Applications -----

    private bool? ApplyApp(PlanItem item, PlatformDeployContext ctx)
    {
        var name = item.ResourceKey;
        ctx.Deployment.Resources.EntraApps.TryGetValue(name, out var app);
        app ??= new EntraAppResource();

        if (item.Action == PlanAction.Delete)
        {
            return DeleteByName(ctx, name, _graph.ResolveApplication(name), id => _graph.DeleteApplication(id), "application");
        }

        var existingId = _graph.ResolveApplication(name);
        if (ctx.DryRun)
        {
            return WouldApply(ctx, item, existingId is not null, "application");
        }

        var body = new JsonObject
        {
            ["displayName"] = name,
            ["signInAudience"] = app.SignInAudience,
        };
        if (app.RedirectUris.Count > 0)
        {
            body["web"] = new JsonObject { ["redirectUris"] = new JsonArray(app.RedirectUris.Select(u => (JsonNode)u!).ToArray()) };
        }
        if (app.IdentifierUris.Count > 0)
        {
            body["identifierUris"] = new JsonArray(app.IdentifierUris.Select(u => (JsonNode)u!).ToArray());
        }
        if (!string.IsNullOrEmpty(app.Description))
        {
            body["notes"] = app.Description;
        }

        if (existingId is not null)
        {
            _graph.UpdateApplication(existingId, body);
            ctx.Console.MarkupLine($"  [yellow]~[/] Updated Entra app: {Markup.Escape(name)}");
            return true;
        }

        var newId = _graph.CreateApplication(body);
        if (string.IsNullOrEmpty(newId))
        {
            return false;
        }
        ctx.RecordRollback(name, newId);
        ctx.Console.MarkupLine($"  [green]+[/] Created Entra app: {Markup.Escape(name)}");

        if (app.CreateServicePrincipal && _graph.ResolveApplicationAppId(newId) is { } appId)
        {
            _graph.EnsureServicePrincipal(appId);
            ctx.Console.MarkupLine($"    [green]+[/] Ensured service principal for {Markup.Escape(name)}");
        }
        return true;
    }

    // ----- Shared helpers -----

    private static bool? DeleteByName(PlatformDeployContext ctx, string name, string? id, Action<string> delete, string kind)
    {
        if (id is null)
        {
            ctx.Console.MarkupLine($"  [dim]=[/] Entra {kind} '{Markup.Escape(name)}' already absent");
            return true;
        }
        if (ctx.DryRun)
        {
            ctx.Console.MarkupLine($"  [red]-[/] Would delete Entra {kind}: {Markup.Escape(name)}");
            return true;
        }
        delete(id);
        ctx.Console.MarkupLine($"  [red]-[/] Deleted Entra {kind}: {Markup.Escape(name)}");
        return true;
    }

    private static bool? WouldApply(PlatformDeployContext ctx, PlanItem item, bool exists, string kind)
    {
        var verb = exists ? "update" : "create";
        var sym = exists ? "[yellow]~[/]" : "[green]+[/]";
        ctx.Console.MarkupLine($"  {sym} Would {verb} Entra {kind}: {Markup.Escape(item.ResourceKey)}");
        return true;
    }

    private static void AttachBind(JsonObject body, string relationship, IEnumerable<string> ids)
    {
        var binds = ids.Select(id => (JsonNode)$"{GraphClient.GraphApiBase}/directoryObjects/{id}").ToArray();
        if (binds.Length > 0)
        {
            body[$"{relationship}@odata.bind"] = new JsonArray(binds);
        }
    }

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c)).ToArray());
        return string.IsNullOrEmpty(cleaned) ? "group" : cleaned;
    }
}
