using System.Net.Sockets;
using Udpi30.Core.Models;

namespace Udpi30.Core.Engine;

/// <summary>Result of probing one connection's source for reachability.</summary>
public sealed class ConnectionCheckResult
{
    public required string Name { get; init; }

    /// <summary>The <c>host:port</c> that was probed, when one could be derived.</summary>
    public string? Target { get; init; }

    /// <summary>False when no connection string or endpoint was available to test.</summary>
    public bool Tested { get; init; }

    /// <summary>True when a TCP connection to the source succeeded.</summary>
    public bool Reachable { get; init; }

    /// <summary>Error message (when unreachable) or skip reason (when not tested).</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Verifies that the data sources behind <c>connections</c> in a <c>udp.yml</c> are
/// reachable. For each connection it derives a <c>host:port</c> from the resolved
/// connection string (via <c>connection_string_var</c> + secrets) or the
/// <c>endpoint</c>, then opens a TCP socket to confirm the source accepts
/// connections. This is a network reachability check — it does not authenticate or
/// run a protocol handshake (that would require a driver per connection type).
/// </summary>
public static class ConnectionChecker
{
    /// <summary>Probe every connection in the deployment.</summary>
    public static List<ConnectionCheckResult> CheckAll(DeploymentDefinition deployment, int timeoutSeconds = 5)
    {
        var results = new List<ConnectionCheckResult>();
        foreach (var (name, conn) in deployment.Connections)
        {
            var connStr = ResolveConnectionString(conn);
            var endpoint = ResolveEndpoint(conn, connStr);
            if (endpoint is null)
            {
                results.Add(new ConnectionCheckResult
                {
                    Name = name,
                    Tested = false,
                    Detail = "no connection string or endpoint to test",
                });
                continue;
            }

            var (host, port) = endpoint.Value;
            var ok = TryConnect(host, port, timeoutSeconds, out var error);
            results.Add(new ConnectionCheckResult
            {
                Name = name,
                Tested = true,
                Target = $"{host}:{port}",
                Reachable = ok,
                Detail = ok ? null : error,
            });
        }
        return results;
    }

    /// <summary>
    /// Resolve the effective connection string for a connection from its
    /// <c>connection_string_var</c> environment variable (or a
    /// <c>connection_string</c> property), resolving any secret references. Returns
    /// null when none is configured.
    /// </summary>
    public static string? ResolveConnectionString(ConnectionConfig conn)
    {
        string? raw = null;
        if (!string.IsNullOrEmpty(conn.ConnectionStringVar))
        {
            raw = Environment.GetEnvironmentVariable(conn.ConnectionStringVar);
        }
        if (string.IsNullOrEmpty(raw))
        {
            foreach (var key in new[] { "connection_string", "connectionString", "connectionstring" })
            {
                if (conn.Properties.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                {
                    raw = v;
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        try
        {
            raw = new SecretsResolver().ResolveString(raw);
        }
        catch
        {
            // Best-effort secret resolution; fall back to the raw value.
        }
        return raw;
    }

    /// <summary>
    /// Derive a <c>(host, port)</c> to probe from a connection string (preferred) or
    /// the connection's <c>endpoint</c>, using the connection type for the default
    /// port. Pure — no I/O — so it is unit-testable. Returns null when no host can
    /// be determined.
    /// </summary>
    public static (string host, int port)? ResolveEndpoint(ConnectionConfig conn, string? connectionString)
    {
        var source = !string.IsNullOrWhiteSpace(connectionString) ? connectionString : conn.Endpoint;
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        string? hostRaw;
        int? port = null;

        if (source.Contains('='))
        {
            var pairs = ParseKeyValue(source);
            if (pairs.TryGetValue("port", out var portVal) && int.TryParse(portVal, out var explicitPort))
            {
                port = explicitPort;
            }
            hostRaw = FirstValue(pairs, "server", "data source", "datasource", "address", "addr",
                "network address", "host", "hostname", "accountendpoint", "endpoint");
        }
        else
        {
            hostRaw = source;
        }

        if (string.IsNullOrWhiteSpace(hostRaw))
        {
            return null;
        }

        var host = NormalizeHost(hostRaw, out var parsedPort);
        port ??= parsedPort;
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        port ??= DefaultPort(conn.Type);
        return (host, port.Value);
    }

    /// <summary>Open a TCP socket to <paramref name="host"/>:<paramref name="port"/> with a timeout.</summary>
    public static bool TryConnect(string host, int port, int timeoutSeconds, out string? error)
    {
        error = null;
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            if (!connectTask.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                error = $"timed out after {timeoutSeconds}s";
                return false;
            }
            if (connectTask.IsFaulted)
            {
                error = connectTask.Exception?.GetBaseException().Message ?? "connection failed";
                return false;
            }
            return client.Connected;
        }
        catch (Exception e)
        {
            error = e.GetBaseException().Message;
            return false;
        }
    }

    private static Dictionary<string, string> ParseKeyValue(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (!result.ContainsKey(key))
            {
                result[key] = value;
            }
        }
        return result;
    }

    private static string? FirstValue(Dictionary<string, string> pairs, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (pairs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static string NormalizeHost(string raw, out int? port)
    {
        port = null;
        raw = raw.Trim();

        if (raw.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[4..];
        }

        // URL form (https://host:port/..., kusto/cosmos endpoints).
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            if (!uri.IsDefaultPort && uri.Port > 0)
            {
                port = uri.Port;
            }
            return uri.Host;
        }

        // host,port (SQL).
        var comma = raw.IndexOf(',');
        if (comma >= 0)
        {
            var portStr = raw[(comma + 1)..].Trim();
            if (int.TryParse(portStr, out var p))
            {
                port = p;
            }
            raw = raw[..comma].Trim();
        }

        // host\instance (named instance) — strip the instance, keep the host.
        var slash = raw.IndexOf('\\');
        if (slash >= 0)
        {
            raw = raw[..slash].Trim();
        }

        return raw;
    }

    private static int DefaultPort(ConnectionType type) => type switch
    {
        ConnectionType.SqlServer => 1433,
        ConnectionType.AzureSql => 1433,
        _ => 443,
    };
}
