using System.Text.Json.Nodes;
using Udpi30.Core.Engine;
using Udpi30.Core.Models;

namespace Udpi30.Core.Tests;

public class AdminApplierTests
{
    private static JsonObject Live(string name, bool enabled, bool canSpecifyGroups = false) => new()
    {
        ["settingName"] = name,
        ["title"] = "Sample",
        ["enabled"] = enabled,
        ["canSpecifySecurityGroups"] = canSpecifyGroups,
    };

    [Fact]
    public void Unknown_Setting_When_Not_In_Tenant()
    {
        var plan = AdminApplier.DiffSetting("NotARealSetting", new TenantSetting { Enabled = true }, current: null);
        Assert.Equal(AdminChangeKind.Unknown, plan.Kind);
    }

    [Fact]
    public void NoChange_When_Enabled_Matches()
    {
        var plan = AdminApplier.DiffSetting("PublishToWeb", new TenantSetting { Enabled = true }, Live("PublishToWeb", enabled: true));
        Assert.Equal(AdminChangeKind.NoChange, plan.Kind);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Update_When_Enabled_Differs()
    {
        var plan = AdminApplier.DiffSetting("PublishToWeb", new TenantSetting { Enabled = false }, Live("PublishToWeb", enabled: true));
        Assert.Equal(AdminChangeKind.Update, plan.Kind);
        Assert.Contains(plan.Changes, c => c.Contains("enabled"));
    }

    [Fact]
    public void Delegate_Only_Diffed_When_Declared()
    {
        // delegate_to_workspace not declared (null) -> not compared even though live omits it.
        var noDelegate = AdminApplier.DiffSetting("X", new TenantSetting { Enabled = true }, Live("X", enabled: true));
        Assert.Equal(AdminChangeKind.NoChange, noDelegate.Kind);

        // declared true, live absent (false) -> change.
        var withDelegate = AdminApplier.DiffSetting("X",
            new TenantSetting { Enabled = true, DelegateToWorkspace = true }, Live("X", enabled: true));
        Assert.Equal(AdminChangeKind.Update, withDelegate.Kind);
        Assert.Contains(withDelegate.Changes, c => c.Contains("delegate_to_workspace"));
    }

    [Fact]
    public void Warns_When_Groups_Declared_But_Unsupported()
    {
        var desired = new TenantSetting
        {
            Enabled = true,
            EnabledSecurityGroups = { new TenantSettingSecurityGroup { GraphId = "g1", Name = "Marketing" } },
        };
        var plan = AdminApplier.DiffSetting("X", desired, Live("X", enabled: true, canSpecifyGroups: false));
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void Groups_Diff_By_GraphId()
    {
        var desired = new TenantSetting
        {
            Enabled = true,
            EnabledSecurityGroups = { new TenantSettingSecurityGroup { GraphId = "g1", Name = "Marketing" } },
        };
        var live = Live("X", enabled: true, canSpecifyGroups: true);
        live["enabledSecurityGroups"] = new JsonArray(new JsonObject { ["graphId"] = "g2", ["name"] = "Other" });

        var plan = AdminApplier.DiffSetting("X", desired, live);
        Assert.Equal(AdminChangeKind.Update, plan.Kind);
        Assert.Contains(plan.Changes, c => c.Contains("enabled_security_groups"));
    }

    [Fact]
    public void BuildRequestBody_Omits_Unset_Optionals()
    {
        var body = AdminApplier.BuildRequestBody(new TenantSetting { Enabled = true });
        Assert.True(body["enabled"]!.GetValue<bool>());
        Assert.False(body.ContainsKey("delegateToCapacity"));
        Assert.False(body.ContainsKey("enabledSecurityGroups"));
        Assert.False(body.ContainsKey("properties"));
    }

    [Fact]
    public void BuildRequestBody_Includes_Declared_Groups_And_Properties()
    {
        var s = new TenantSetting
        {
            Enabled = true,
            DelegateToCapacity = true,
            EnabledSecurityGroups = { new TenantSettingSecurityGroup { GraphId = "g1", Name = "Marketing" } },
            Properties = { new TenantSettingProperty { Name = "CreateP2w", Value = "true", Type = "Boolean" } },
        };
        var body = AdminApplier.BuildRequestBody(s);
        Assert.True(body["delegateToCapacity"]!.GetValue<bool>());
        Assert.Equal("g1", body["enabledSecurityGroups"]![0]!["graphId"]!.GetValue<string>());
        Assert.Equal("CreateP2w", body["properties"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Plan_Maps_Settings_By_Name()
    {
        var admin = new AdminConfig
        {
            TenantSettings =
            {
                ["PublishToWeb"] = new TenantSetting { Enabled = true },
                ["Bogus"] = new TenantSetting { Enabled = true },
            },
        };
        var live = new Dictionary<string, JsonObject>
        {
            ["PublishToWeb"] = Live("PublishToWeb", enabled: false),
        };

        var plan = AdminApplier.Plan(admin, live);
        Assert.True(plan.HasChanges);
        Assert.True(plan.HasUnknown);
    }
}
