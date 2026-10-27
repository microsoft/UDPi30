using System.Text;
using Spectre.Console;
using Udpi30.Core.Engine;
using Udpi30.Core.Models;

namespace Udpi30.Core.Providers.Platforms;

/// <summary>
/// Deploys Azure (ARM) resources through Bicep via the <c>az</c> CLI. Resource
/// groups and storage accounts are emitted as generated Bicep templates;
/// arbitrary resources use an author-supplied <c>.bicep</c> file. Deployments are
/// incremental and idempotent (re-applying converges), so create and update share
/// a path.
/// </summary>
public sealed class AzureResourceProvider : IResourcePlatformProvider
{
    private readonly AzureCli _az;

    public AzureResourceProvider(AzureCli? az = null) => _az = az ?? new AzureCli();

    public ResourcePlatform Platform => ResourcePlatform.Azure;

    public bool? Apply(PlanItem item, PlatformDeployContext ctx)
    {
        // Dispatch by field name (not ARM type): the three bespoke types keep
        // their dedicated paths; every other azure_* service flows through the
        // generic single-resource emitter. Keying on the field avoids ARM-type
        // collisions (e.g. the storage-family services share a storage account
        // type with azure_storage_accounts).
        var field = ctx.Deployment.Resources.GetResourceType(item.ResourceKey);
        return field switch
        {
            "azure_resource_groups" => ApplyResourceGroup(item, ctx),
            "azure_storage_accounts" => ApplyStorageAccount(item, ctx),
            "azure_deployments" => ApplyBicep(item, ctx),
            null => Skip(ctx, item, "resource not found in deployment"),
            _ => ApplyGenericService(item, ctx, field),
        };
    }

    // ----- Resource groups (subscription-scope Bicep) -----

    private bool? ApplyResourceGroup(PlanItem item, PlatformDeployContext ctx)
    {
        var name = item.ResourceKey;
        ctx.Deployment.Resources.AzureResourceGroups.TryGetValue(name, out var rg);
        rg ??= new AzureResourceGroupResource();
        var sub = rg.Subscription ?? ctx.Deployment.Azure.Subscription;
        var location = rg.Location ?? ctx.Deployment.Azure.Location;

        if (item.Action == PlanAction.Delete)
        {
            return DeleteRg(ctx, name, sub);
        }
        if (string.IsNullOrEmpty(location))
        {
            return Skip(ctx, item, "no location (set azure.location or the resource's location)");
        }
        if (ctx.DryRun)
        {
            return Would(ctx, "resource group", $"{name} in {location}");
        }

        var bicep = new StringBuilder()
            .AppendLine("targetScope = 'subscription'")
            .AppendLine($"resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {{")
            .AppendLine($"  name: '{Esc(name)}'")
            .AppendLine($"  location: '{Esc(location)}'")
            .AppendLine($"  tags: {TagsBicep(rg.Tags)}")
            .AppendLine("}")
            .ToString();

        return DeployTemplate(ctx, bicep, name, sub, args =>
        {
            args.Add("deployment");
            args.Add("sub");
            args.Add("create");
            args.Add("--location");
            args.Add(location);
        }, recordKey: name);
    }

    // ----- Storage accounts (resource-group-scope Bicep) -----

    private bool? ApplyStorageAccount(PlanItem item, PlatformDeployContext ctx)
    {
        var name = item.ResourceKey;
        ctx.Deployment.Resources.AzureStorageAccounts.TryGetValue(name, out var sa);
        if (sa is null || string.IsNullOrEmpty(sa.ResourceGroup))
        {
            return Skip(ctx, item, "storage account requires a resource_group");
        }
        var sub = sa.Subscription ?? ctx.Deployment.Azure.Subscription;
        var location = sa.Location ?? ctx.Deployment.Azure.Location;

        if (item.Action == PlanAction.Delete)
        {
            return DeleteStorage(ctx, name, sa.ResourceGroup, sub);
        }
        if (string.IsNullOrEmpty(location))
        {
            return Skip(ctx, item, "no location (set azure.location or the resource's location)");
        }
        if (ctx.DryRun)
        {
            return Would(ctx, "storage account", $"{name} in {sa.ResourceGroup}");
        }

        var bicep = new StringBuilder()
            .AppendLine($"resource sa 'Microsoft.Storage/storageAccounts@2023-01-01' = {{")
            .AppendLine($"  name: '{Esc(name)}'")
            .AppendLine($"  location: '{Esc(location)}'")
            .AppendLine($"  sku: {{ name: '{Esc(sa.Sku)}' }}")
            .AppendLine($"  kind: '{Esc(sa.Kind)}'")
            .AppendLine("  properties: {")
            .AppendLine($"    accessTier: '{Esc(sa.AccessTier)}'")
            .AppendLine($"    supportsHttpsTrafficOnly: {(sa.HttpsOnly ? "true" : "false")}")
            .AppendLine("  }")
            .AppendLine($"  tags: {TagsBicep(sa.Tags)}")
            .AppendLine("}")
            .ToString();

        return DeployTemplate(ctx, bicep, name, sub, args =>
        {
            args.Add("deployment");
            args.Add("group");
            args.Add("create");
            args.Add("--resource-group");
            args.Add(sa.ResourceGroup);
        }, recordKey: null);
    }

    // ----- Generic author-supplied Bicep -----

    private bool? ApplyBicep(PlanItem item, PlatformDeployContext ctx)
    {
        var name = item.ResourceKey;
        ctx.Deployment.Resources.AzureDeployments.TryGetValue(name, out var dep);
        if (dep is null || string.IsNullOrEmpty(dep.TemplateFile))
        {
            return Skip(ctx, item, "generic Azure deployment requires a template_file");
        }
        if (item.Action == PlanAction.Delete)
        {
            return Skip(ctx, item, "delete is not supported for generic Bicep deployments — remove the resources by hand or via their resource group");
        }

        var sub = dep.Subscription ?? ctx.Deployment.Azure.Subscription;
        var templatePath = Path.IsPathRooted(dep.TemplateFile)
            ? dep.TemplateFile
            : Path.Combine(ctx.ProjectDir, dep.TemplateFile);
        if (!File.Exists(templatePath))
        {
            return Skip(ctx, item, $"template file not found: {templatePath}");
        }

        var isSub = string.Equals(dep.Scope, "subscription", StringComparison.OrdinalIgnoreCase);
        if (isSub && string.IsNullOrEmpty(dep.Location ?? ctx.Deployment.Azure.Location))
        {
            return Skip(ctx, item, "subscription-scope deployment needs a location");
        }
        if (!isSub && string.IsNullOrEmpty(dep.ResourceGroup))
        {
            return Skip(ctx, item, "group-scope deployment needs a resource_group");
        }
        if (ctx.DryRun)
        {
            return Would(ctx, "bicep deployment", $"{name} ({dep.Scope})");
        }

        var args = new List<string> { "deployment", isSub ? "sub" : "group", "create" };
        if (isSub)
        {
            args.Add("--location");
            args.Add(dep.Location ?? ctx.Deployment.Azure.Location!);
        }
        else
        {
            args.Add("--resource-group");
            args.Add(dep.ResourceGroup!);
        }
        AddCommon(args, sub, name, templatePath);
        foreach (var (k, v) in dep.Parameters)
        {
            args.Add("--parameters");
            args.Add($"{k}={v}");
        }

        _az.RunChecked(args);
        ctx.Console.MarkupLine($"  [green]+[/] Deployed Azure (bicep): {Markup.Escape(name)}");
        return true;
    }

    // ----- Generic Azure service (single ARM resource, group-scope Bicep) -----

    /// <summary>ARM API version per resource type for the generic service emitter.</summary>
    private static readonly Dictionary<string, string> ApiVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.DataFactory/factories"] = "2018-06-01",
        ["Microsoft.Databricks/workspaces"] = "2024-05-01",
        ["Microsoft.EventHub/namespaces"] = "2024-01-01",
        ["Microsoft.EventGrid/topics"] = "2022-06-15",
        ["Microsoft.StreamAnalytics/streamingjobs"] = "2020-03-01",
        ["Microsoft.Devices/IotHubs"] = "2023-06-30",
        ["Microsoft.Logic/workflows"] = "2019-05-01",
        ["Microsoft.Web/sites"] = "2023-12-01",
        ["Microsoft.Storage/storageAccounts"] = "2023-01-01",
        ["Microsoft.Sql/servers/databases"] = "2023-08-01",
        ["Microsoft.Sql/managedInstances"] = "2023-08-01",
        ["Microsoft.SqlVirtualMachine/sqlVirtualMachines"] = "2023-10-01",
        ["Microsoft.DBforPostgreSQL/flexibleServers"] = "2022-12-01",
        ["Microsoft.DBforMySQL/flexibleServers"] = "2023-06-30",
        ["Microsoft.DBforMariaDB/servers"] = "2018-06-01",
        ["Microsoft.DocumentDB/databaseAccounts"] = "2024-05-15",
        ["Microsoft.Cache/Redis"] = "2023-08-01",
        ["Microsoft.DataBox/jobs"] = "2022-12-01",

        // Governance / security / AI
        ["Microsoft.Purview/accounts"] = "2021-12-01",
        ["Microsoft.KeyVault/vaults"] = "2023-07-01",
        ["Microsoft.Authorization/policyAssignments"] = "2022-06-01",
        ["Microsoft.Security/pricings"] = "2024-01-01",
        ["Microsoft.MachineLearningServices/workspaces"] = "2024-04-01",
        ["Microsoft.SecurityInsights/onboardingStates"] = "2024-03-01",
        ["Microsoft.VideoIndexer/accounts"] = "2024-01-01",
        ["Microsoft.CognitiveServices/accounts"] = "2024-10-01",
        ["Microsoft.LoadTestService/loadTests"] = "2022-12-01",
        ["Microsoft.ManagedIdentity/userAssignedIdentities"] = "2023-01-31",
        ["Microsoft.Management/serviceGroups"] = "2024-02-01-preview",

        // Monitoring / observability
        ["Microsoft.Insights/components"] = "2020-02-02",
        ["Microsoft.Insights/metricAlerts"] = "2018-03-01",
        ["Microsoft.Insights/diagnosticSettings"] = "2021-05-01-preview",
        ["Microsoft.OperationalInsights/workspaces"] = "2023-09-01",
        ["Microsoft.Insights/dataCollectionRules"] = "2023-03-11",
        ["Microsoft.Insights/workbooks"] = "2023-06-01",
        ["Microsoft.DatabaseWatcher/watchers"] = "2023-09-01-preview",

        // Networking
        ["Microsoft.Network/networkWatchers"] = "2023-11-01",
        ["Microsoft.Network/dnsZones"] = "2018-05-01",
        ["Microsoft.Network/networkInterfaces"] = "2023-11-01",
        ["Microsoft.Network/privateDnsZones"] = "2020-06-01",
        ["Microsoft.Network/publicIPAddresses"] = "2023-11-01",
        ["Microsoft.Network/routeTables"] = "2023-11-01",
        ["Microsoft.Network/virtualNetworks"] = "2023-11-01",
        ["Microsoft.Network/localNetworkGateways"] = "2023-11-01",
        ["Microsoft.Network/peeringServices"] = "2022-10-01",
        ["Microsoft.Peering/peerings"] = "2022-10-01",
        ["Microsoft.Network/virtualNetworkGateways"] = "2023-11-01",
        ["Microsoft.Network/virtualWans"] = "2023-11-01",
        ["Microsoft.Network/ddosProtectionPlans"] = "2023-11-01",
        ["Microsoft.Network/azureFirewalls"] = "2023-11-01",
        ["Microsoft.Network/ipGroups"] = "2023-11-01",
        ["Microsoft.Network/networkSecurityGroups"] = "2023-11-01",
        ["Microsoft.Network/applicationGateways"] = "2023-11-01",
        ["Microsoft.Network/applicationSecurityGroups"] = "2023-11-01",
    };

    private bool? ApplyGenericService(PlanItem item, PlatformDeployContext ctx, string field)
    {
        var name = item.ResourceKey;
        var armType = item.ResourceType;
        if (ctx.Deployment.Resources.GetResourceObject(field, name) is not AzureServiceResource svc
            || string.IsNullOrEmpty(svc.ResourceGroup))
        {
            return Skip(ctx, item, $"{field} requires a resource_group");
        }
        if (!ApiVersions.TryGetValue(armType, out var apiVersion))
        {
            return Skip(ctx, item, $"no API version registered for '{armType}'");
        }

        var sub = svc.Subscription ?? ctx.Deployment.Azure.Subscription;
        var location = svc.Location ?? ctx.Deployment.Azure.Location;

        if (item.Action == PlanAction.Delete)
        {
            return DeleteGeneric(ctx, name, armType, svc.ResourceGroup, sub);
        }
        if (string.IsNullOrEmpty(location))
        {
            return Skip(ctx, item, "no location (set azure.location or the resource's location)");
        }
        if (ctx.DryRun)
        {
            return Would(ctx, armType, $"{name} in {svc.ResourceGroup}");
        }

        var sb = new StringBuilder()
            .AppendLine($"resource res '{armType}@{apiVersion}' = {{")
            .AppendLine($"  name: '{Esc(name)}'")
            .AppendLine($"  location: '{Esc(location)}'");
        if (!string.IsNullOrEmpty(svc.Sku))
        {
            sb.AppendLine($"  sku: {{ name: '{Esc(svc.Sku)}' }}");
        }
        if (!string.IsNullOrEmpty(svc.Kind))
        {
            sb.AppendLine($"  kind: '{Esc(svc.Kind)}'");
        }
        if (svc.Properties.Count > 0)
        {
            sb.AppendLine($"  properties: {ToBicep(svc.Properties, 1)}");
        }
        if (svc.Tags.Count > 0)
        {
            sb.AppendLine($"  tags: {TagsBicep(svc.Tags)}");
        }
        sb.AppendLine("}");

        return DeployTemplate(ctx, sb.ToString(), name, sub, args =>
        {
            args.Add("deployment");
            args.Add("group");
            args.Add("create");
            args.Add("--resource-group");
            args.Add(svc.ResourceGroup);
        }, recordKey: null);
    }

    private bool? DeleteGeneric(PlatformDeployContext ctx, string name, string armType, string rg, string? sub)
    {
        if (ctx.DryRun)
        {
            ctx.Console.MarkupLine($"  [red]-[/] Would delete Azure {armType}: {Markup.Escape(name)}");
            return true;
        }
        var args = new List<string> { "resource", "delete", "--name", name, "--resource-group", rg, "--resource-type", armType };
        if (!string.IsNullOrEmpty(sub))
        {
            args.Add("--subscription");
            args.Add(sub);
        }
        _az.RunChecked(args);
        ctx.Console.MarkupLine($"  [red]-[/] Deleted Azure {armType}: {Markup.Escape(name)}");
        return true;
    }

    /// <summary>Serialize a YAML-deserialized value to a Bicep literal.</summary>
    private static string ToBicep(object? node, int indent)
    {
        switch (node)
        {
            case null:
                return "null";
            case bool b:
                return b ? "true" : "false";
            case string s:
                return Scalar(s);
            case System.Collections.IDictionary map:
            {
                if (map.Count == 0)
                {
                    return "{}";
                }
                var pad = new string(' ', (indent + 1) * 2);
                var close = new string(' ', indent * 2);
                var lines = new List<string>();
                foreach (System.Collections.DictionaryEntry e in map)
                {
                    var key = e.Key?.ToString() ?? "";
                    lines.Add($"{pad}{BicepKey(key)}: {ToBicep(e.Value, indent + 1)}");
                }
                return "{\n" + string.Join("\n", lines) + "\n" + close + "}";
            }
            case System.Collections.IEnumerable seq:
            {
                var items = seq.Cast<object?>().Select(v => ToBicep(v, indent)).ToList();
                return items.Count == 0 ? "[]" : "[ " + string.Join(", ", items) + " ]";
            }
            default:
                return Scalar(node.ToString() ?? "");
        }
    }

    private static string Scalar(string s)
    {
        if (s is "true" or "false" or "null")
        {
            return s;
        }
        if (long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _)
            || double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return s;
        }
        return $"'{Esc(s)}'";
    }

    private static readonly System.Text.RegularExpressions.Regex IdentifierPattern =
        new("^[A-Za-z_][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string BicepKey(string key) => IdentifierPattern.IsMatch(key) ? key : $"'{Esc(key)}'";

    // ----- Shared helpers -----

    private bool? DeployTemplate(PlatformDeployContext ctx, string bicep, string name, string? sub,
        Action<List<string>> scopeArgs, string? recordKey)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"udp-{Sanitize(name)}-{Guid.NewGuid():N}.bicep");
        File.WriteAllText(tempPath, bicep);
        try
        {
            var args = new List<string>();
            scopeArgs(args);
            AddCommon(args, sub, name, tempPath);
            _az.RunChecked(args);
            if (recordKey is not null)
            {
                ctx.RecordRollback(recordKey, name);
            }
            ctx.Console.MarkupLine($"  [green]+[/] Deployed Azure: {Markup.Escape(name)}");
            return true;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    private static void AddCommon(List<string> args, string? sub, string name, string templatePath)
    {
        args.Add("--name");
        args.Add($"udp-{Sanitize(name)}");
        args.Add("--template-file");
        args.Add(templatePath);
        if (!string.IsNullOrEmpty(sub))
        {
            args.Add("--subscription");
            args.Add(sub);
        }
        args.Add("--only-show-errors");
    }

    private bool? DeleteRg(PlatformDeployContext ctx, string name, string? sub)
    {
        if (ctx.DryRun)
        {
            ctx.Console.MarkupLine($"  [red]-[/] Would delete Azure resource group: {Markup.Escape(name)}");
            return true;
        }
        var args = new List<string> { "group", "delete", "--name", name, "--yes" };
        if (!string.IsNullOrEmpty(sub))
        {
            args.Add("--subscription");
            args.Add(sub);
        }
        _az.RunChecked(args);
        ctx.Console.MarkupLine($"  [red]-[/] Deleted Azure resource group: {Markup.Escape(name)}");
        return true;
    }

    private bool? DeleteStorage(PlatformDeployContext ctx, string name, string rg, string? sub)
    {
        if (ctx.DryRun)
        {
            ctx.Console.MarkupLine($"  [red]-[/] Would delete Azure storage account: {Markup.Escape(name)}");
            return true;
        }
        var args = new List<string> { "storage", "account", "delete", "--name", name, "--resource-group", rg, "--yes" };
        if (!string.IsNullOrEmpty(sub))
        {
            args.Add("--subscription");
            args.Add(sub);
        }
        _az.RunChecked(args);
        ctx.Console.MarkupLine($"  [red]-[/] Deleted Azure storage account: {Markup.Escape(name)}");
        return true;
    }

    private static bool? Would(PlatformDeployContext ctx, string kind, string detail)
    {
        ctx.Console.MarkupLine($"  [green]+[/] Would deploy Azure {kind}: {Markup.Escape(detail)}");
        return true;
    }

    private static bool? Skip(PlatformDeployContext ctx, PlanItem item, string why)
    {
        ctx.Console.MarkupLine($"  [yellow]![/] {Markup.Escape(item.ResourceKey)}: {Markup.Escape(why)} — skipping");
        return null;
    }

    private static string TagsBicep(Dictionary<string, string> tags) =>
        tags.Count == 0
            ? "{}"
            : "{\n" + string.Join("\n", tags.Select(t => $"    '{Esc(t.Key)}': '{Esc(t.Value)}'")) + "\n  }";

    private static string Esc(string value) => value.Replace("'", "\\'");

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "deployment" : cleaned;
    }
}
