using Azure.Core;
using Azure.Identity;

namespace Udpi30.Core.Providers;

/// <summary>
/// Authentication configuration for the Fabric API. Mirrors the Python
/// <c>FabricAuth</c> dataclass: explicit service-principal credentials when all
/// three values are present, otherwise interactive browser or the default
/// credential chain (managed identity / <c>az login</c>).
/// </summary>
public sealed class FabricAuth
{
    public const string FabricScope = "https://api.fabric.microsoft.com/.default";

    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? TenantId { get; init; }
    public bool UseBrowser { get; init; }

    /// <summary>Explicit credential override (used for dependency injection and tests).</summary>
    public TokenCredential? Credential { get; init; }

    private TokenCredential? _credential;

    /// <summary>Build a <see cref="FabricAuth"/> from the standard AZURE_* environment variables.</summary>
    public static FabricAuth FromEnvironment() => new()
    {
        ClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"),
        ClientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET"),
        TenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID"),
        UseBrowser = string.Equals(
            Environment.GetEnvironmentVariable("FABRIC_USE_BROWSER"), "true", StringComparison.OrdinalIgnoreCase),
    };

    public TokenCredential GetCredential()
    {
        if (_credential is not null)
        {
            return _credential;
        }

        if (Credential is not null)
        {
            _credential = Credential;
        }
        else if (!string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret) && !string.IsNullOrEmpty(TenantId))
        {
            _credential = new ClientSecretCredential(TenantId, ClientId, ClientSecret);
        }
        else if (UseBrowser)
        {
            _credential = new InteractiveBrowserCredential();
        }
        else
        {
            _credential = new DefaultAzureCredential();
        }
        return _credential;
    }

    /// <summary>Get an access token for the given scope (defaults to the Fabric scope).</summary>
    public string GetToken(string scope = FabricScope)
    {
        var token = GetCredential().GetToken(new TokenRequestContext([scope]), CancellationToken.None);
        return token.Token;
    }
}
