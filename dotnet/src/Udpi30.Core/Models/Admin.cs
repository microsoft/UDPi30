namespace Udpi30.Core.Models;

/// <summary>
/// A security group reference on a tenant setting. Mirrors the Fabric Admin API
/// <c>TenantSettingSecurityGroup</c> (graphId + name).
/// </summary>
public sealed class TenantSettingSecurityGroup
{
    public string GraphId { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// A typed property on a tenant setting. Mirrors the Fabric Admin API
/// <c>TenantSettingProperty</c>. <see cref="Type"/> is one of FreeText, Url,
/// Boolean, MailEnabledSecurityGroup, Integer.
/// </summary>
public sealed class TenantSettingProperty
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Type { get; set; } = "Boolean";
}

/// <summary>
/// Declarative desired state for a single Fabric tenant (admin) setting. Maps to
/// the Fabric Admin API <c>Update Tenant Setting</c> request body. Keyed in
/// <see cref="AdminConfig.TenantSettings"/> by the API <c>settingName</c>
/// (e.g. <c>PublishToWeb</c>), not the portal display title.
/// </summary>
public sealed class TenantSetting
{
    /// <summary>Enable (true) or disable (false) the setting.</summary>
    public bool Enabled { get; set; }

    /// <summary>Allow a capacity admin to override this setting. Null = leave unchanged.</summary>
    public bool? DelegateToCapacity { get; set; }

    /// <summary>Allow a domain admin to override this setting. Null = leave unchanged.</summary>
    public bool? DelegateToDomain { get; set; }

    /// <summary>Allow a workspace admin to override this setting. Null = leave unchanged.</summary>
    public bool? DelegateToWorkspace { get; set; }

    /// <summary>Security groups the setting is enabled for (when not enabled org-wide).</summary>
    public List<TenantSettingSecurityGroup> EnabledSecurityGroups { get; set; } = [];

    /// <summary>Security groups explicitly excluded from the setting.</summary>
    public List<TenantSettingSecurityGroup> ExcludedSecurityGroups { get; set; } = [];

    /// <summary>Extra typed properties some settings require (name/value/type).</summary>
    public List<TenantSettingProperty> Properties { get; set; } = [];
}

/// <summary>
/// Tenant/admin-level configuration. Unlike <see cref="ResourcesConfig"/>, these
/// apply tenant-wide via the Fabric Admin API — they are not workspace items and
/// are never deployed per-target. Applied only through the gated
/// <c>udpi30 admin apply</c> command.
/// </summary>
public sealed class AdminConfig
{
    /// <summary>Tenant settings keyed by their API <c>settingName</c>.</summary>
    public Dictionary<string, TenantSetting> TenantSettings { get; set; } = [];
}
