using System.Text.Json.Nodes;
using Spectre.Console;
using Udpi30.Core.Models;
using Udpi30.Core.Providers;

namespace Udpi30.Core.Engine;

/// <summary>Outcome of diffing one declared tenant setting against the live tenant.</summary>
public enum AdminChangeKind
{
    /// <summary>Declared state already matches the tenant.</summary>
    NoChange,

    /// <summary>Declared state differs and would be applied.</summary>
    Update,

    /// <summary>The settingName does not exist in this tenant (likely a typo or wrong identifier).</summary>
    Unknown,
}

/// <summary>Plan for a single tenant setting.</summary>
public sealed class AdminSettingPlan
{
    public required string SettingName { get; init; }
    public string? Title { get; init; }
    public AdminChangeKind Kind { get; init; }
    public List<string> Changes { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public JsonObject RequestBody { get; init; } = [];
}

/// <summary>Plan across all declared tenant settings.</summary>
public sealed class AdminPlan
{
    public List<AdminSettingPlan> Items { get; } = [];
    public bool HasChanges => Items.Exists(i => i.Kind == AdminChangeKind.Update);
    public bool HasUnknown => Items.Exists(i => i.Kind == AdminChangeKind.Unknown);
}

/// <summary>
/// Plans and applies tenant-level (admin) settings via the Fabric Admin API. Setting
/// names are validated against the live tenant — the docs index lists display titles,
/// not the API <c>settingName</c> identifiers, so the live List Tenant Settings call is
/// the authoritative source. Only settings the user explicitly declares are touched;
/// security groups and properties are managed only when declared, never cleared implicitly.
/// </summary>
public static class AdminApplier
{
    /// <summary>Fabric caps tenant-setting updates at 25/min; pace applies safely under that.</summary>
    private const int ApplyPaceMs = 2500;

    /// <summary>Build the Update Tenant Setting request body from declared desired state.</summary>
    public static JsonObject BuildRequestBody(TenantSetting s)
    {
        var body = new JsonObject { ["enabled"] = s.Enabled };
        if (s.DelegateToCapacity is bool dc) body["delegateToCapacity"] = dc;
        if (s.DelegateToDomain is bool dd) body["delegateToDomain"] = dd;
        if (s.DelegateToWorkspace is bool dw) body["delegateToWorkspace"] = dw;
        if (s.EnabledSecurityGroups.Count > 0) body["enabledSecurityGroups"] = GroupsArray(s.EnabledSecurityGroups);
        if (s.ExcludedSecurityGroups.Count > 0) body["excludedSecurityGroups"] = GroupsArray(s.ExcludedSecurityGroups);
        if (s.Properties.Count > 0)
        {
            body["properties"] = new JsonArray(s.Properties
                .Select(p => (JsonNode)new JsonObject { ["name"] = p.Name, ["value"] = p.Value, ["type"] = p.Type })
                .ToArray());
        }
        return body;
    }

    private static JsonArray GroupsArray(IEnumerable<TenantSettingSecurityGroup> groups) =>
        new(groups.Select(g => (JsonNode)new JsonObject { ["graphId"] = g.GraphId, ["name"] = g.Name }).ToArray());

    /// <summary>
    /// Pure diff of one declared setting against its live state (null when the setting
    /// does not exist in the tenant). No I/O — used directly by tests.
    /// </summary>
    public static AdminSettingPlan DiffSetting(string name, TenantSetting desired, JsonObject? current)
    {
        var body = BuildRequestBody(desired);

        if (current is null)
        {
            return new AdminSettingPlan
            {
                SettingName = name,
                Kind = AdminChangeKind.Unknown,
                Changes = { "setting not found in this tenant — check the settingName (use the API identifier, e.g. PublishToWeb)" },
                RequestBody = body,
            };
        }

        var changes = new List<string>();
        var warnings = new List<string>();

        var curEnabled = current["enabled"]?.GetValue<bool>() ?? false;
        if (curEnabled != desired.Enabled)
        {
            changes.Add($"enabled: {Bool(curEnabled)} -> {Bool(desired.Enabled)}");
        }

        DiffDelegate("delegate_to_capacity", desired.DelegateToCapacity, current["delegateToCapacity"], changes);
        DiffDelegate("delegate_to_domain", desired.DelegateToDomain, current["delegateToDomain"], changes);
        DiffDelegate("delegate_to_workspace", desired.DelegateToWorkspace, current["delegateToWorkspace"], changes);

        var canSpecify = current["canSpecifySecurityGroups"]?.GetValue<bool>() ?? false;
        if ((desired.EnabledSecurityGroups.Count > 0 || desired.ExcludedSecurityGroups.Count > 0) && !canSpecify)
        {
            warnings.Add("this setting does not support security groups (canSpecifySecurityGroups=false); declared groups will be ignored by Fabric");
        }

        DiffGroups("enabled_security_groups", desired.EnabledSecurityGroups, current["enabledSecurityGroups"], changes);
        DiffGroups("excluded_security_groups", desired.ExcludedSecurityGroups, current["excludedSecurityGroups"], changes);
        DiffProperties(desired.Properties, current["properties"], changes);

        return new AdminSettingPlan
        {
            SettingName = name,
            Title = current["title"]?.GetValue<string>(),
            Kind = changes.Count > 0 ? AdminChangeKind.Update : AdminChangeKind.NoChange,
            Changes = changes,
            Warnings = warnings,
            RequestBody = body,
        };
    }

    /// <summary>Plan all declared settings against a map of live settings keyed by settingName.</summary>
    public static AdminPlan Plan(AdminConfig admin, IReadOnlyDictionary<string, JsonObject> liveByName)
    {
        var plan = new AdminPlan();
        foreach (var (name, desired) in admin.TenantSettings)
        {
            liveByName.TryGetValue(name, out var current);
            plan.Items.Add(DiffSetting(name, desired, current));
        }
        return plan;
    }

    /// <summary>Fetch live settings from the tenant and plan against them.</summary>
    public static AdminPlan Plan(FabricClient client, AdminConfig admin)
    {
        var liveByName = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in client.ListTenantSettings())
        {
            if (node is JsonObject obj && obj["settingName"]?.GetValue<string>() is { } sn)
            {
                liveByName[sn] = obj;
            }
        }
        return Plan(admin, liveByName);
    }

    /// <summary>Apply all settings that have changes. Skips NoChange and Unknown items.</summary>
    public static (int applied, int failed) Apply(FabricClient client, AdminPlan plan, IAnsiConsole console, bool dryRun)
    {
        var toApply = plan.Items.Where(i => i.Kind == AdminChangeKind.Update).ToList();
        var applied = 0;
        var failed = 0;

        for (var i = 0; i < toApply.Count; i++)
        {
            var item = toApply[i];
            if (dryRun)
            {
                console.MarkupLine($"  [dim](dry-run)[/] would update [bold]{Markup.Escape(item.SettingName)}[/]");
                continue;
            }

            try
            {
                client.UpdateTenantSetting(item.SettingName, item.RequestBody);
                console.MarkupLine($"  [green]~[/] Updated: {Markup.Escape(item.SettingName)}");
                applied++;
            }
            catch (Exception e)
            {
                console.MarkupLine($"  [red]ERROR[/] {Markup.Escape(item.SettingName)}: {Markup.Escape(e.Message)}");
                failed++;
            }

            // Respect the Admin API's 25 requests/minute limit.
            if (i < toApply.Count - 1)
            {
                Thread.Sleep(ApplyPaceMs);
            }
        }

        return (applied, failed);
    }

    private static void DiffDelegate(string label, bool? desired, JsonNode? current, List<string> changes)
    {
        if (desired is not bool want)
        {
            return;
        }
        var have = current?.GetValue<bool>() ?? false;
        if (have != want)
        {
            changes.Add($"{label}: {Bool(have)} -> {Bool(want)}");
        }
    }

    private static void DiffGroups(string label, List<TenantSettingSecurityGroup> desired, JsonNode? current, List<string> changes)
    {
        if (desired.Count == 0)
        {
            return; // only managed when declared
        }
        var want = desired.Select(g => g.GraphId).Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var have = (current as JsonArray)?
            .Select(n => n?["graphId"]?.GetValue<string>() ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList() ?? [];

        if (!want.SequenceEqual(have))
        {
            var wantNames = string.Join(", ", desired.Select(g => g.Name));
            changes.Add($"{label}: [{have.Count} group(s)] -> [{wantNames}]");
        }
    }

    private static void DiffProperties(List<TenantSettingProperty> desired, JsonNode? current, List<string> changes)
    {
        if (desired.Count == 0)
        {
            return; // only managed when declared
        }
        var have = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (current is JsonArray arr)
        {
            foreach (var n in arr)
            {
                if (n?["name"]?.GetValue<string>() is { } pn)
                {
                    have[pn] = n["value"]?.GetValue<string>() ?? "";
                }
            }
        }
        foreach (var p in desired)
        {
            if (!have.TryGetValue(p.Name, out var cur) || cur != p.Value)
            {
                changes.Add($"property '{p.Name}': '{cur ?? "(unset)"}' -> '{p.Value}'");
            }
        }
    }

    private static string Bool(bool b) => b ? "enabled" : "disabled";
}
