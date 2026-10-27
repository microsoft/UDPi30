using Udpi30.Core.Providers;

namespace Udpi30.Ontology;

/// <summary>Small helpers for constructing a Fabric client and resolving a workspace.</summary>
public static class FabricSession
{
    /// <summary>
    /// Build a Fabric client. When <paramref name="useBrowser"/> is set and no
    /// service-principal environment variables are present, authentication falls
    /// back to an interactive browser sign-in; otherwise the default credential
    /// chain (<c>az login</c> / managed identity / service principal) is used.
    /// </summary>
    public static FabricClient CreateClient(bool useBrowser) =>
        new(new FabricAuth
        {
            ClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"),
            ClientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET"),
            TenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID"),
            UseBrowser = useBrowser,
        });

    /// <summary>Resolve a workspace given a GUID or a display name. Returns (id, name).</summary>
    public static (string? Id, string? Name) ResolveWorkspace(FabricClient client, string workspaceNameOrId)
    {
        if (IsGuid(workspaceNameOrId))
        {
            var ws = client.GetWorkspace(workspaceNameOrId);
            return (workspaceNameOrId, ws["displayName"]?.GetValue<string>());
        }

        var match = client.FindWorkspace(workspaceNameOrId);
        return (match?["id"]?.GetValue<string>(), match?["displayName"]?.GetValue<string>() ?? workspaceNameOrId);
    }

    public static bool IsGuid(string s) => Guid.TryParse(s, out _);
}
