namespace Udpi30.Core.Models;

// ---------------------------------------------------------------------------
// Microsoft Entra (Azure AD) directory objects — deployed via Microsoft Graph
// at tenant scope. The resource key is used as the object's displayName.
// ---------------------------------------------------------------------------

/// <summary>
/// An Entra security group (Graph <c>/groups</c>). The resource key is the
/// <c>displayName</c>. Owners/members are display names, UPNs, or GUIDs and are
/// resolved to object ids at deploy time.
/// </summary>
public sealed class EntraGroupResource
{
    public string? Description { get; set; }

    /// <summary>Mail nickname (alias). Defaults to a sanitized form of the key when omitted.</summary>
    public string? MailNickname { get; set; }

    /// <summary>Security-enabled group (the default; the only kind udpi30 creates).</summary>
    public bool SecurityEnabled { get; set; } = true;

    /// <summary>Mail-enabled group. Left false for pure security groups.</summary>
    public bool MailEnabled { get; set; }

    /// <summary>Whether the group can be assigned to Entra roles (privileged).</summary>
    public bool AssignableToRole { get; set; }

    /// <summary>Owners — display names, UPNs, or object GUIDs.</summary>
    public List<string> Owners { get; set; } = [];

    /// <summary>Members — display names, UPNs, or object GUIDs.</summary>
    public List<string> Members { get; set; } = [];
}

/// <summary>
/// An Entra application registration (Graph <c>/applications</c>). The resource
/// key is the <c>displayName</c>. Optionally provisions a matching service
/// principal so the app can be granted access.
/// </summary>
public sealed class EntraAppResource
{
    public string? Description { get; set; }

    /// <summary>One of AzureADMyOrg, AzureADMultipleOrgs, AzureADandPersonalMicrosoftAccount, PersonalMicrosoftAccount.</summary>
    public string SignInAudience { get; set; } = "AzureADMyOrg";

    /// <summary>Web redirect URIs.</summary>
    public List<string> RedirectUris { get; set; } = [];

    /// <summary>Application ID (identifier) URIs, e.g. <c>api://contoso-app</c>.</summary>
    public List<string> IdentifierUris { get; set; } = [];

    /// <summary>Provision a service principal for the app after creation.</summary>
    public bool CreateServicePrincipal { get; set; } = true;
}
