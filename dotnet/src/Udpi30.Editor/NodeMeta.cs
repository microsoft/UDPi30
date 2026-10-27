namespace Udpi30.Editor;

/// <summary>Classifies a tree node so the form knows how to edit, rename and remove it.</summary>
public enum NodeKind
{
    /// <summary>A non-editable grouping node.</summary>
    Container,
    Deployment,
    Workspace,
    Azure,
    Variable,
    ResourceType,
    Resource,
    Connection,
    Target,
    TenantSetting,
    Security,
    Policies,
    Notifications,
    State,
}

/// <summary>Payload stored on each <see cref="TreeNode.Tag"/>.</summary>
public sealed class NodeMeta
{
    public NodeKind Kind { get; init; }

    /// <summary>Object shown in the property grid when this node is selected (may be null).</summary>
    public object? Target { get; init; }

    /// <summary>Dictionary key, for renamable/removable nodes.</summary>
    public string? Key { get; set; }

    /// <summary>snake_case resource field name (for <see cref="NodeKind.Resource"/>).</summary>
    public string? ResourceField { get; init; }

    public bool CanRename => Kind is NodeKind.Variable or NodeKind.Resource
        or NodeKind.Connection or NodeKind.Target or NodeKind.TenantSetting;

    public bool CanRemove => CanRename;
}
