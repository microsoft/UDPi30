using System.Text.RegularExpressions;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace Udpi30.Core.Engine;

/// <summary>
/// Resolves secret references in deployment configuration:
/// <c>${secret.ENV_VAR_NAME}</c> (environment variable) and
/// <c>${keyvault.vault-name.secret-name}</c> (Azure Key Vault).
/// Mirrors <c>engine/secrets.py</c>.
/// </summary>
public sealed partial class SecretsResolver
{
    [GeneratedRegex(@"\$\{secret\.([^}]+)\}")]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"\$\{keyvault\.([^.]+)\.([^}]+)\}")]
    private static partial Regex KeyVaultPattern();

    private readonly Dictionary<string, string> _cache = [];
    private readonly Dictionary<string, SecretClient> _clients = [];
    private readonly Func<string, SecretClient>? _clientFactory;

    public SecretsResolver(Func<string, SecretClient>? clientFactory = null)
    {
        _clientFactory = clientFactory;
    }

    private SecretClient GetKeyVaultClient(string vaultName)
    {
        if (_clientFactory is not null)
        {
            return _clientFactory(vaultName);
        }
        if (_clients.TryGetValue(vaultName, out var existing))
        {
            return existing;
        }
        var client = new SecretClient(
            new Uri($"https://{vaultName}.vault.azure.net"),
            new DefaultAzureCredential());
        _clients[vaultName] = client;
        return client;
    }

    public static string ResolveEnvSecret(string varName)
    {
        var value = Environment.GetEnvironmentVariable(varName);
        if (value is null)
        {
            throw new InvalidOperationException($"Secret environment variable '{varName}' not set");
        }
        return value;
    }

    public string ResolveKeyVaultSecret(string vaultName, string secretName)
    {
        var cacheKey = $"{vaultName}/{secretName}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }
        var client = GetKeyVaultClient(vaultName);
        var value = client.GetSecret(secretName).Value.Value;
        _cache[cacheKey] = value;
        return value;
    }

    /// <summary>Resolve all secret references in a string.</summary>
    public string ResolveString(string value)
    {
        var result = KeyVaultPattern().Replace(value, m =>
            ResolveKeyVaultSecret(m.Groups[1].Value, m.Groups[2].Value));
        result = SecretPattern().Replace(result, m => ResolveEnvSecret(m.Groups[1].Value));
        return result;
    }

    /// <summary>Recursively resolve all secret references in a dictionary graph.</summary>
    public Dictionary<string, object?> ResolveDict(Dictionary<string, object?> data)
    {
        var resolved = new Dictionary<string, object?>();
        foreach (var (key, value) in data)
        {
            resolved[key] = value switch
            {
                string s => ResolveString(s),
                Dictionary<string, object?> d => ResolveDict(d),
                List<object?> l => l.Select(v => v is string s ? ResolveString(s) : v).ToList(),
                _ => value,
            };
        }
        return resolved;
    }
}
