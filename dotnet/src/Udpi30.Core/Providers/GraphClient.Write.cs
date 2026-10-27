using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Udpi30.Core.Providers;

/// <summary>Raised when a Microsoft Graph write call fails.</summary>
public sealed class GraphApiError(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public override string ToString() => $"Graph API Error ({StatusCode}): {Message}";
}

/// <summary>
/// Write half of <see cref="GraphClient"/> — create/update/delete for the Entra
/// directory objects udpi30 manages (security groups and app registrations).
/// Unlike the read half, these surface failures as <see cref="GraphApiError"/>
/// rather than returning <c>null</c>, so the deployer can report them.
/// </summary>
public sealed partial class GraphClient
{
    /// <summary>Send a request with an optional JSON body, retrying once on 401. Throws on >= 400.</summary>
    private JsonNode? SendJson(string method, string path, JsonNode? body)
    {
        HttpRequestMessage Build()
        {
            var req = new HttpRequestMessage(new HttpMethod(method), GraphApiBase + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetToken());
            if (body is not null)
            {
                req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            return req;
        }

        var resp = _http.Send(Build());
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            _token = null;
            resp.Dispose();
            resp = _http.Send(Build());
        }
        using (resp)
        {
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if ((int)resp.StatusCode >= 400)
            {
                throw new GraphApiError((int)resp.StatusCode, $"{method} {path}: {text}");
            }
            return string.IsNullOrEmpty(text) ? null : JsonNode.Parse(text);
        }
    }

    /// <summary>Resolve an application registration's object id by display name.</summary>
    public string? ResolveApplication(string displayName)
    {
        if (IsGuid(displayName))
        {
            return displayName;
        }
        var result = Request("GET", "/applications",
            new Dictionary<string, string> { ["$filter"] = $"displayName eq '{displayName}'", ["$select"] = "id" });
        return (result?["value"] as JsonArray)?.FirstOrDefault()?["id"]?.GetValue<string>();
    }

    /// <summary>Resolve an application's <c>appId</c> (client id) from its object id.</summary>
    public string? ResolveApplicationAppId(string objectId) =>
        Request("GET", $"/applications/{objectId}", new Dictionary<string, string> { ["$select"] = "appId" })?
            ["appId"]?.GetValue<string>();

    /// <summary>Create a security group. Returns the new object id.</summary>
    public string? CreateGroup(JsonObject body) => SendJson("POST", "/groups", body)?["id"]?.GetValue<string>();

    /// <summary>
    /// Add a member to a group by object id. Returns true if added or already a
    /// member; Graph reports an existing member with HTTP 400, which is treated
    /// as success so the operation stays idempotent.
    /// </summary>
    public bool AddGroupMember(string groupId, string memberObjectId)
    {
        var body = new JsonObject { ["@odata.id"] = $"{GraphApiBase}/directoryObjects/{memberObjectId}" };
        try
        {
            SendJson("POST", $"/groups/{groupId}/members/$ref", body);
            return true;
        }
        catch (GraphApiError e) when (e.StatusCode == 400 && e.Message.Contains("already exist", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    /// <summary>Patch an existing group.</summary>
    public void UpdateGroup(string objectId, JsonObject body) => SendJson("PATCH", $"/groups/{objectId}", body);

    /// <summary>Delete a group by object id.</summary>
    public void DeleteGroup(string objectId) => SendJson("DELETE", $"/groups/{objectId}", null);

    /// <summary>Create an application registration. Returns the new object id.</summary>
    public string? CreateApplication(JsonObject body) => SendJson("POST", "/applications", body)?["id"]?.GetValue<string>();

    /// <summary>Patch an existing application registration.</summary>
    public void UpdateApplication(string objectId, JsonObject body) => SendJson("PATCH", $"/applications/{objectId}", body);

    /// <summary>Delete an application registration by object id.</summary>
    public void DeleteApplication(string objectId) => SendJson("DELETE", $"/applications/{objectId}", null);

    /// <summary>Ensure a service principal exists for an app (by appId). Returns its object id.</summary>
    public string? EnsureServicePrincipal(string appId)
    {
        var existing = Request("GET", "/servicePrincipals",
            new Dictionary<string, string> { ["$filter"] = $"appId eq '{appId}'", ["$select"] = "id" });
        var existingId = (existing?["value"] as JsonArray)?.FirstOrDefault()?["id"]?.GetValue<string>();
        if (existingId is not null)
        {
            return existingId;
        }
        return SendJson("POST", "/servicePrincipals", new JsonObject { ["appId"] = appId })?["id"]?.GetValue<string>();
    }
}
