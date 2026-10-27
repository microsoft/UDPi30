namespace Udpi30.Core.Models;

/// <summary>OneLake security role binding.</summary>
public sealed class OneLakeRoleBinding
{
    public List<string> Tables { get; set; } = [];
    public List<string> Folders { get; set; } = [];
    public List<OneLakePermission> Permissions { get; set; } = [];
}

/// <summary>Security role definition.</summary>
public sealed class SecurityRole
{
    public string Name { get; set; } = "";
    public string? EntraGroup { get; set; }
    public string? EntraUser { get; set; }
    public string? ServicePrincipal { get; set; }
    public WorkspaceRole WorkspaceRole { get; set; } = WorkspaceRole.Viewer;
    public List<OneLakeRoleBinding> OnelakeRoles { get; set; } = [];
}

/// <summary>Security configuration.</summary>
public sealed class SecurityConfig
{
    public List<SecurityRole> Roles { get; set; } = [];
}

/// <summary>Connection / data source configuration.</summary>
public sealed class ConnectionConfig
{
    public ConnectionType Type { get; set; }
    public string? Endpoint { get; set; }
    public string? Database { get; set; }
    public string? AuthMethod { get; set; }
    public string? ConnectionStringVar { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
}
