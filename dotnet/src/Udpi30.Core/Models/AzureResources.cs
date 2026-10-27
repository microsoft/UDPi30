namespace Udpi30.Core.Models;

// ---------------------------------------------------------------------------
// Azure (ARM) resources — deployed via Bicep through the `az` CLI. The resource
// key is the Azure resource name. Subscription/location default from the
// top-level `azure:` block and can be overridden per resource.
// ---------------------------------------------------------------------------

/// <summary>Deployment-wide Azure defaults (top-level <c>azure:</c> block).</summary>
public sealed class AzureConfig
{
    /// <summary>Default subscription id (or name) for Azure deployments.</summary>
    public string? Subscription { get; set; }

    /// <summary>Default Azure region, e.g. <c>eastus</c>.</summary>
    public string? Location { get; set; }
}

/// <summary>
/// An Azure resource group. Deployed via a subscription-scope Bicep deployment
/// (<c>az deployment sub create</c>), so resource-group creation is itself Bicep.
/// </summary>
public sealed class AzureResourceGroupResource
{
    public string? Subscription { get; set; }
    public string? Location { get; set; }
    public Dictionary<string, string> Tags { get; set; } = [];
}

/// <summary>
/// An Azure Storage account, deployed via a resource-group-scope Bicep
/// deployment into <see cref="ResourceGroup"/>.
/// </summary>
public sealed class AzureStorageAccountResource
{
    public string? Subscription { get; set; }
    public string ResourceGroup { get; set; } = "";
    public string? Location { get; set; }
    public string Sku { get; set; } = "Standard_LRS";
    public string Kind { get; set; } = "StorageV2";
    public string AccessTier { get; set; } = "Hot";
    public bool HttpsOnly { get; set; } = true;
    public Dictionary<string, string> Tags { get; set; } = [];
}

/// <summary>
/// A first-class Azure service resource, deployed as a single ARM resource via a
/// generated resource-group-scope Bicep template. The ARM type and API version
/// come from the resource registry; <see cref="Sku"/>, <see cref="Kind"/>,
/// <see cref="Properties"/>, and <see cref="Tags"/> are emitted into the template.
/// Used by every <c>azure_*</c> service type (Data Factory, Databricks, Event
/// Hubs, SQL, Cosmos DB, …). For full control over a template, use
/// <see cref="AzureBicepResource"/> (<c>azure_deployments</c>) instead.
/// </summary>
public class AzureServiceResource
{
    public string? Subscription { get; set; }

    /// <summary>Target resource group (required).</summary>
    public string ResourceGroup { get; set; } = "";

    public string? Location { get; set; }

    /// <summary>Optional SKU name, emitted as <c>sku: { name: '…' }</c>.</summary>
    public string? Sku { get; set; }

    /// <summary>Optional resource <c>kind</c> (e.g. storage account kind).</summary>
    public string? Kind { get; set; }

    /// <summary>ARM <c>properties</c> bag, emitted verbatim into the template.</summary>
    public Dictionary<string, object?> Properties { get; set; } = [];

    public Dictionary<string, string> Tags { get; set; } = [];
}

/// <summary>
/// A generic Bicep deployment — the escape hatch for any Azure resource type the
/// author supplies as a <c>.bicep</c> file. Deployed at subscription or
/// resource-group scope.
/// </summary>
public sealed class AzureBicepResource
{
    /// <summary><c>group</c> (default) or <c>subscription</c>.</summary>
    public string Scope { get; set; } = "group";
    public string? Subscription { get; set; }

    /// <summary>Required for <c>group</c> scope.</summary>
    public string? ResourceGroup { get; set; }

    /// <summary>Required for <c>subscription</c> scope.</summary>
    public string? Location { get; set; }

    /// <summary>Path (relative to the project) to the <c>.bicep</c> template.</summary>
    public string TemplateFile { get; set; } = "";

    /// <summary>Template parameters passed as <c>key=value</c> to <c>az deployment</c>.</summary>
    public Dictionary<string, object?> Parameters { get; set; } = [];
}
