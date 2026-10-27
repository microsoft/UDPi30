using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Udpi30.Core.Providers;

/// <summary>
/// Client for the Microsoft Fabric REST API — workspace and item CRUD plus the
/// auxiliary operations needed for deployment. Mirrors <c>providers/udp_api.py</c>.
/// Synchronous, matching the Python client's structure.
/// </summary>
public sealed class FabricClient
{
    public const string FabricApiBase = "https://api.fabric.microsoft.com/v1";
    private static readonly Uri FabricApiUri = new(FabricApiBase + "/");

    // Type-specific create endpoints (item type -> URL segment).
    private static readonly IReadOnlyDictionary<string, string> TypeEndpoints = new Dictionary<string, string>
    {
        ["Lakehouse"] = "lakehouses",
        ["Notebook"] = "notebooks",
        ["Warehouse"] = "warehouses",
        ["SemanticModel"] = "semanticModels",
        ["Report"] = "reports",
        ["DataPipeline"] = "dataPipelines",
        ["Environment"] = "environments",
        ["Eventhouse"] = "eventhouses",
        ["Eventstream"] = "eventstreams",
        ["MLModel"] = "mlModels",
        ["MLExperiment"] = "mlExperiments",
        ["DataAgent"] = "dataAgents",
        ["KQLDatabase"] = "kqlDatabases",
        ["KQLDashboard"] = "kqlDashboards",
        ["KQLQueryset"] = "kqlQuerysets",
        ["SparkJobDefinition"] = "sparkJobDefinitions",
        ["GraphQLApi"] = "graphqlApis",
        ["Reflex"] = "reflexes",
        ["CopyJob"] = "copyJobs",
        ["MountedDataFactory"] = "mountedDataFactories",
        ["SnowflakeDatabase"] = "snowflakeDatabases",
        ["DataBuildToolJob"] = "dataBuildToolJobs",
        ["Ontology"] = "ontologies",
        ["MirroredDatabase"] = "mirroredDatabases",
        ["MirroredAzureDatabricksCatalog"] = "mirroredAzureDatabricksCatalogs",
        ["DigitalTwinBuilder"] = "digitalTwinBuilders",
        ["DigitalTwinBuilderFlow"] = "digitalTwinBuilderFlows",
        ["GraphQuerySet"] = "graphQuerySets",
        ["HLSCohort"] = "hlsCohorts",
        ["Dataflow"] = "dataflows",
        ["VariableLibrary"] = "variableLibraries",
        ["UserDataFunction"] = "userDataFunctions",
        ["ApacheAirflowJob"] = "apacheAirflowJobs",
        ["SQLDatabase"] = "sqlDatabases",
        ["CosmosDBDatabase"] = "cosmosDBDatabases",
        ["OperationsAgent"] = "operationsAgents",
        ["AnomalyDetector"] = "anomalyDetectors",
        ["EventSchemaSet"] = "eventSchemaSets",
        ["Map"] = "maps",
        ["GraphModel"] = "graphModels",
        ["Graph"] = "graphs",
    };

    public FabricAuth Auth { get; }
    private readonly HttpClient _http;
    private string? _token;

    public FabricClient(FabricAuth? auth = null, HttpClient? httpClient = null)
    {
        Auth = auth ?? FabricAuth.FromEnvironment();
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    private string Token => _token ??= Auth.GetToken();

    // -- core request --------------------------------------------------------

    /// <summary>Make an authenticated API request with retry logic. Returns parsed JSON or null.</summary>
    public JsonNode? Request(string method, string path, object? data = null,
        IDictionary<string, string>? queryParams = null, int retryCount = 3)
    {
        var url = BuildUrl(FabricApiBase + path, queryParams);

        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            HttpResponseMessage resp;
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                if (data is not null)
                {
                    request.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
                }
                resp = _http.Send(request);
            }
            catch (HttpRequestException e)
            {
                if (attempt == retryCount - 1)
                {
                    throw new FabricApiError(0, e.Message);
                }
                Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                continue;
            }

            using (resp)
            {
                var status = (int)resp.StatusCode;
                var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (status == 401)
                {
                    _token = Auth.GetToken();
                    continue;
                }

                if (status == 429)
                {
                    var retryAfter = resp.Headers.RetryAfter?.Delta?.TotalSeconds
                        ?? Math.Min(30, Math.Pow(2, attempt));
                    Thread.Sleep(TimeSpan.FromSeconds(retryAfter));
                    continue;
                }

                if (status >= 400)
                {
                    JsonNode? errorBody = string.IsNullOrEmpty(text) ? null : SafeParse(text);
                    var msg = errorBody?["message"]?.GetValue<string>()
                        ?? errorBody?["error"]?["message"]?.GetValue<string>()
                        ?? text;
                    var isRetriable = errorBody?["isRetriable"]?.GetValue<bool>() ?? false;
                    if (isRetriable && attempt < retryCount - 1)
                    {
                        var wait = Math.Min(60, Math.Pow(2, attempt + 1) * 5);
                        Thread.Sleep(TimeSpan.FromSeconds(wait));
                        continue;
                    }
                    var requestId = resp.Headers.TryGetValues("x-ms-request-id", out var ids) ? ids.FirstOrDefault() : null;
                    throw new FabricApiError(status, msg, requestId);
                }

                if (status == 204)
                {
                    return null;
                }

                if (status == 202)
                {
                    var location = resp.Headers.Location?.ToString();
                    var operationId = resp.Headers.TryGetValues("x-ms-operation-id", out var operationIds)
                        ? operationIds.FirstOrDefault()
                        : null;
                    var operationUrl = NormalizeOperationUrl(location, operationId)
                        ?? throw new FabricApiError(status,
                            "Fabric accepted the request but did not return a safe long-running operation URL.");
                    var retry = resp.Headers.RetryAfter?.Delta?.TotalSeconds.ToString() ?? "5";
                    return new JsonObject
                    {
                        ["operation_url"] = operationUrl,
                        ["operation_id"] = operationId,
                        ["retry_after"] = retry,
                    };
                }

                return string.IsNullOrEmpty(text) ? null : SafeParse(text);
            }
        }

        throw new FabricApiError(0, "Max retries exceeded");
    }

    /// <summary>Poll a long-running operation until completion.</summary>
    public JsonNode? WaitForOperation(string operationUrl, int timeout = 300)
    {
        var operationUri = ValidateOperationUri(operationUrl);
        var start = DateTime.UtcNow;
        var transientFailures = 0;
        while ((DateTime.UtcNow - start).TotalSeconds < timeout)
        {
            HttpResponseMessage resp;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, operationUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                resp = _http.Send(request);
            }
            catch (HttpRequestException e)
            {
                transientFailures++;
                if (transientFailures >= 3)
                {
                    throw new FabricApiError(0, e.Message);
                }
                SleepForPoll(start, timeout, TimeSpan.FromSeconds(Math.Pow(2, transientFailures - 1)));
                continue;
            }

            using (resp)
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    transientFailures++;
                    if (transientFailures >= 3)
                    {
                        throw new FabricApiError((int)resp.StatusCode,
                            "Authentication failed while polling the Fabric operation.");
                    }
                    _token = Auth.GetToken();
                    continue;
                }

                if (resp.StatusCode == HttpStatusCode.TooManyRequests
                    || (int)resp.StatusCode >= 500)
                {
                    transientFailures++;
                    if (transientFailures >= 3)
                    {
                        throw new FabricApiError((int)resp.StatusCode, ReadErrorMessage(resp));
                    }
                    var retryDelay = resp.Headers.RetryAfter?.Delta
                        ?? TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, transientFailures - 1)));
                    SleepForPoll(start, timeout, retryDelay);
                    continue;
                }

                transientFailures = 0;
                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var result = SafeParse(text);
                    var statusStr = result?["status"]?.GetValue<string>()?.ToLowerInvariant() ?? "";
                    if (statusStr is "succeeded" or "completed")
                    {
                        return result;
                    }
                    if (statusStr is "failed" or "cancelled")
                    {
                        var errMsg = result?["error"]?["message"]?.GetValue<string>() ?? "Unknown error";
                        throw new FabricApiError((int)resp.StatusCode, $"Operation {statusStr}: {errMsg}");
                    }
                }
                else if ((int)resp.StatusCode >= 400)
                {
                    throw new FabricApiError((int)resp.StatusCode, ReadErrorMessage(resp));
                }

                SleepForPoll(start, timeout, resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5));
            }
        }
        throw new FabricApiError(0, $"Operation timed out after {timeout}s");
    }

    private static string ReadErrorMessage(HttpResponseMessage response)
    {
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var errorBody = string.IsNullOrEmpty(text) ? null : SafeParse(text);
        return errorBody?["message"]?.GetValue<string>()
            ?? errorBody?["error"]?["message"]?.GetValue<string>()
            ?? text;
    }

    private static void SleepForPoll(DateTime start, int timeout, TimeSpan requestedDelay)
    {
        var remaining = TimeSpan.FromSeconds(timeout) - (DateTime.UtcNow - start);
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }
        Thread.Sleep(requestedDelay < remaining ? requestedDelay : remaining);
    }

    /// <summary>Retrieve the result produced by a completed long-running operation.</summary>
    public JsonNode? GetOperationResult(string operationUrl)
    {
        var operationUri = ValidateOperationUri(operationUrl);
        var resultPath = ToApiPath(operationUri).TrimEnd('/') + "/result";
        return Request("GET", resultPath);
    }

    /// <summary>Poll a long-running operation and retrieve its final result.</summary>
    public JsonNode? WaitForOperationResult(string operationUrl, int timeout = 300)
    {
        WaitForOperation(operationUrl, timeout);
        return GetOperationResult(operationUrl);
    }

    private static string? NormalizeOperationUrl(string? location, string? operationId)
    {
        if (!string.IsNullOrWhiteSpace(operationId))
        {
            return $"{FabricApiBase}/operations/{Uri.EscapeDataString(operationId)}";
        }

        if (string.IsNullOrWhiteSpace(location)
            || !Uri.TryCreate(location, UriKind.RelativeOrAbsolute, out var locationUri))
        {
            return null;
        }

        var absoluteUri = locationUri.IsAbsoluteUri ? locationUri : new Uri(FabricApiUri, locationUri);
        return IsFabricOperationUri(absoluteUri) ? absoluteUri.GetLeftPart(UriPartial.Path) : null;
    }

    private static Uri ValidateOperationUri(string operationUrl)
    {
        if (!Uri.TryCreate(operationUrl, UriKind.Absolute, out var operationUri)
            || !IsFabricOperationUri(operationUri))
        {
            throw new FabricApiError(0,
                "Fabric long-running operations must use the api.fabric.microsoft.com HTTPS endpoint.");
        }
        return operationUri;
    }

    private static bool IsFabricOperationUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, FabricApiUri.Host, StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath.StartsWith("/v1/operations/", StringComparison.Ordinal);

    private static string ToApiPath(Uri uri)
    {
        const string versionPrefix = "/v1";
        return uri.PathAndQuery[versionPrefix.Length..];
    }

    // -- workspaces ----------------------------------------------------------

    public List<JsonNode> ListWorkspaces() => ValueList(Request("GET", "/workspaces"));

    public JsonObject GetWorkspace(string workspaceId) =>
        Request("GET", $"/workspaces/{workspaceId}")?.AsObject() ?? [];

    public JsonObject? FindWorkspace(string name)
    {
        foreach (var ws in ListWorkspaces())
        {
            if (string.Equals(ws["displayName"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase))
            {
                return ws.AsObject();
            }
        }
        return null;
    }

    public JsonObject CreateWorkspace(string name, string? capacityId = null, string? description = null)
    {
        var body = new JsonObject { ["displayName"] = name };
        if (!string.IsNullOrEmpty(capacityId)) body["capacityId"] = capacityId;
        if (!string.IsNullOrEmpty(description)) body["description"] = description;
        return Request("POST", "/workspaces", body)?.AsObject() ?? [];
    }

    public void AssignCapacity(string workspaceId, string capacityId) =>
        Request("POST", $"/workspaces/{workspaceId}/assignToCapacity", new JsonObject { ["capacityId"] = capacityId });

    public void DeleteWorkspace(string workspaceId) => Request("DELETE", $"/workspaces/{workspaceId}");

    // -- items ---------------------------------------------------------------

    public List<JsonNode> ListItems(string workspaceId, string? itemType = null)
    {
        var query = itemType is not null ? new Dictionary<string, string> { ["type"] = itemType } : null;
        return ValueList(Request("GET", $"/workspaces/{workspaceId}/items", queryParams: query));
    }

    public JsonObject GetItem(string workspaceId, string itemId) =>
        Request("GET", $"/workspaces/{workspaceId}/items/{itemId}")?.AsObject() ?? [];

    public JsonObject CreateItem(string workspaceId, string displayName, string itemType,
        JsonNode? definition = null, string? description = null,
        JsonNode? creationPayload = null, string? folderId = null)
    {
        var body = new JsonObject { ["displayName"] = displayName };
        if (!string.IsNullOrEmpty(description)) body["description"] = description;
        if (definition is not null) body["definition"] = definition.DeepClone();
        if (creationPayload is not null) body["creationPayload"] = creationPayload.DeepClone();
        if (!string.IsNullOrEmpty(folderId)) body["folderId"] = folderId;

        string path;
        if (TypeEndpoints.TryGetValue(itemType, out var endpoint))
        {
            path = $"/workspaces/{workspaceId}/{endpoint}";
        }
        else
        {
            body["type"] = itemType;
            path = $"/workspaces/{workspaceId}/items";
        }
        return Request("POST", path, body)?.AsObject() ?? [];
    }

    public JsonObject UpdateItem(string workspaceId, string itemId, string? displayName = null, string? description = null)
    {
        var body = new JsonObject();
        if (!string.IsNullOrEmpty(displayName)) body["displayName"] = displayName;
        if (!string.IsNullOrEmpty(description)) body["description"] = description;
        return Request("PATCH", $"/workspaces/{workspaceId}/items/{itemId}", body)?.AsObject() ?? [];
    }

    public JsonNode? UpdateItemDefinition(string workspaceId, string itemId, JsonNode definition)
    {
        var result = Request("POST", $"/workspaces/{workspaceId}/items/{itemId}/updateDefinition",
            new JsonObject { ["definition"] = definition.DeepClone() });
        var opUrl = result?["operation_url"]?.GetValue<string>();
        return opUrl is not null ? WaitForOperation(opUrl) : result;
    }

    public void DeleteItem(string workspaceId, string itemId) =>
        Request("DELETE", $"/workspaces/{workspaceId}/items/{itemId}");

    public JsonObject GetItemDefinition(string workspaceId, string itemId)
    {
        var result = Request("POST", $"/workspaces/{workspaceId}/items/{itemId}/getDefinition");
        var opUrl = result?["operation_url"]?.GetValue<string>();
        if (opUrl is not null)
        {
            return WaitForOperationResult(opUrl)?.AsObject() ?? [];
        }
        return result?.AsObject() ?? [];
    }

    // -- folders -------------------------------------------------------------

    public JsonObject CreateFolder(string workspaceId, string displayName) =>
        Request("POST", $"/workspaces/{workspaceId}/folders", new JsonObject { ["displayName"] = displayName })?.AsObject() ?? [];

    public List<JsonNode> ListFolders(string workspaceId) =>
        ValueList(Request("GET", $"/workspaces/{workspaceId}/folders"));

    // -- shortcuts -----------------------------------------------------------

    public JsonObject CreateShortcut(string workspaceId, string itemId, string shortcutName, string path,
        JsonNode target, JsonNode? transform = null)
    {
        var body = new JsonObject { ["name"] = shortcutName, ["path"] = path, ["target"] = target.DeepClone() };
        if (transform is not null) body["transform"] = transform.DeepClone();
        return Request("POST", $"/workspaces/{workspaceId}/items/{itemId}/shortcuts", body)?.AsObject() ?? [];
    }

    public List<JsonNode> ListShortcuts(string workspaceId, string itemId) =>
        ValueList(Request("GET", $"/workspaces/{workspaceId}/items/{itemId}/shortcuts"));

    public void DeleteShortcut(string workspaceId, string itemId, string shortcutName, string shortcutPath) =>
        Request("DELETE", $"/workspaces/{workspaceId}/items/{itemId}/shortcuts/{shortcutPath}/{shortcutName}");

    // -- data access roles / role assignments --------------------------------

    public JsonNode? UpdateLakehouseDataAccessRoles(string workspaceId, string itemId, JsonArray roles) =>
        Request("PUT", $"/workspaces/{workspaceId}/items/{itemId}/dataAccessRoles", new JsonObject { ["value"] = roles.DeepClone() });

    public List<JsonNode> ListWorkspaceRoleAssignments(string workspaceId) =>
        ValueList(Request("GET", $"/workspaces/{workspaceId}/roleAssignments"));

    public JsonObject AddWorkspaceRoleAssignment(string workspaceId, string principalId, string principalType, string role)
    {
        var body = new JsonObject
        {
            ["principal"] = new JsonObject { ["id"] = principalId, ["type"] = principalType },
            ["role"] = role,
        };
        return Request("POST", $"/workspaces/{workspaceId}/roleAssignments", body)?.AsObject() ?? [];
    }

    // -- semantic model / environment ----------------------------------------

    public JsonNode? RefreshSemanticModel(string workspaceId, string itemId)
    {
        var result = Request("POST", $"/workspaces/{workspaceId}/semanticModels/{itemId}/refresh");
        var opUrl = result?["operation_url"]?.GetValue<string>();
        return opUrl is not null ? WaitForOperation(opUrl, timeout: 600) : result;
    }

    public JsonObject? PublishEnvironment(string workspaceId, string itemId)
    {
        var url = $"{FabricApiBase}/workspaces/{workspaceId}/environments/{itemId}/staging/publish?beta=False";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var resp = _http.Send(request);
        var status = (int)resp.StatusCode;
        if (status is 200 or 202)
        {
            return new JsonObject { ["status"] = "publish_triggered" };
        }
        if (status >= 400)
        {
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var errMsg = string.IsNullOrEmpty(text) ? $"HTTP {status}" : text[..Math.Min(300, text.Length)];
            throw new InvalidOperationException($"Environment publish failed: {errMsg}");
        }
        return null;
    }

    public JsonObject? UpdateEnvironmentLibraries(string workspaceId, string itemId, IEnumerable<string> libraries)
    {
        var ymlLines = new List<string> { "name: udp-env", "dependencies:" };
        ymlLines.AddRange(libraries.Select(lib => $"  - {lib}"));
        var content = Encoding.UTF8.GetBytes(string.Join("\n", ymlLines));

        var url = $"{FabricApiBase}/workspaces/{workspaceId}/environments/{itemId}/staging/libraries/importExternalLibraries";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Auth.GetToken());
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var resp = _http.Send(request);
        var status = (int)resp.StatusCode;
        if (status is 200 or 202)
        {
            return new JsonObject { ["status"] = "uploaded" };
        }
        if (status >= 400)
        {
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var errMsg = string.IsNullOrEmpty(text) ? $"HTTP {status}" : text[..Math.Min(500, text.Length)];
            throw new InvalidOperationException($"Environment library upload failed: {errMsg}");
        }
        return null;
    }

    public JsonNode? UpdateItemTags(string workspaceId, string itemId, JsonArray tags) =>
        Request("POST", $"/workspaces/{workspaceId}/items/{itemId}/tags", new JsonObject { ["tags"] = tags.DeepClone() });

    // -- git -----------------------------------------------------------------

    public JsonNode? ConnectWorkspaceToGit(string workspaceId, string provider, string organization,
        string? project, string repository, string branch = "main", string directory = "/")
    {
        var details = new JsonObject
        {
            ["organizationName"] = organization,
            ["repositoryName"] = repository,
            ["branchName"] = branch,
            ["directoryName"] = directory,
        };
        if (!string.IsNullOrEmpty(project)) details["projectName"] = project;
        return Request("POST", $"/workspaces/{workspaceId}/git/connect", new JsonObject { ["gitProviderDetails"] = details });
    }

    public JsonNode? InitializeGitConnection(string workspaceId)
    {
        var result = Request("POST", $"/workspaces/{workspaceId}/git/initializeConnection", new JsonObject());
        var opUrl = result?["operation_url"]?.GetValue<string>();
        return opUrl is not null ? WaitForOperation(opUrl) : result;
    }

    public JsonNode? GetGitStatus(string workspaceId) => Request("GET", $"/workspaces/{workspaceId}/git/status");

    public void DisconnectWorkspaceFromGit(string workspaceId) =>
        Request("POST", $"/workspaces/{workspaceId}/git/disconnect");

    // -- connections ---------------------------------------------------------

    public List<JsonNode> ListConnections() => ValueList(Request("GET", "/connections"));

    public JsonObject CreateConnection(string displayName, string connectionType,
        string connectivityType = "ShareableCloud", JsonNode? connectionDetails = null, JsonNode? credentialDetails = null)
    {
        var body = new JsonObject
        {
            ["displayName"] = displayName,
            ["connectivityType"] = connectivityType,
            ["connectionDetails"] = connectionDetails?.DeepClone() ?? new JsonObject(),
        };
        if (credentialDetails is not null) body["credentialDetails"] = credentialDetails.DeepClone();
        return Request("POST", "/connections", body)?.AsObject() ?? [];
    }

    public void DeleteConnection(string connectionId) => Request("DELETE", $"/connections/{connectionId}");

    // -- admin / tenant settings ---------------------------------------------

    /// <summary>
    /// List all tenant settings (Admin API). Requires a Fabric administrator or a
    /// service principal with <c>Tenant.Read.All</c>. The response is keyed under
    /// <c>tenantSettings</c> rather than the usual <c>value</c> array.
    /// </summary>
    public List<JsonNode> ListTenantSettings()
    {
        var result = Request("GET", "/admin/tenantsettings");
        if (result?["tenantSettings"] is JsonArray arr)
        {
            return arr.Where(n => n is not null).Select(n => n!).ToList();
        }
        return [];
    }

    /// <summary>
    /// Update a single tenant setting by its <c>settingName</c> (Admin API, preview).
    /// Requires a Fabric administrator or a service principal with
    /// <c>Tenant.ReadWrite.All</c>. Rate-limited by Fabric to 25 requests/minute.
    /// </summary>
    public JsonNode? UpdateTenantSetting(string settingName, JsonObject body) =>
        Request("POST", $"/admin/tenantsettings/{settingName}/update", body);

    // -- jobs ----------------------------------------------------------------

    public JsonNode? RunItemJob(string workspaceId, string itemId, string jobType, JsonNode? executionData = null)
    {
        JsonObject? body = null;
        if (executionData is not null)
        {
            body = new JsonObject { ["executionData"] = executionData.DeepClone() };
        }
        return Request("POST", $"/workspaces/{workspaceId}/items/{itemId}/jobs/instances?jobType={jobType}", body);
    }

    public JsonObject GetItemJobInstance(string workspaceId, string itemId, string jobInstanceId) =>
        Request("GET", $"/workspaces/{workspaceId}/items/{itemId}/jobs/instances/{jobInstanceId}")?.AsObject() ?? [];

    public JsonNode? ExecuteSql(string workspaceId, string warehouseId, string sql) =>
        Request("POST", $"/workspaces/{workspaceId}/warehouses/{warehouseId}/executeQuery",
            new JsonObject { ["query"] = sql, ["maxRows"] = 1000 });

    public JsonNode? ExecuteLakehouseSql(string workspaceId, string sqlEndpointId, string sql) =>
        Request("POST", $"/workspaces/{workspaceId}/sqlEndpoints/{sqlEndpointId}/executeQuery",
            new JsonObject { ["query"] = sql, ["maxRows"] = 1000 });

    // -- scheduling ----------------------------------------------------------

    public JsonObject CreateItemSchedule(string workspaceId, string itemId, JsonNode scheduleConfig) =>
        Request("POST", $"/workspaces/{workspaceId}/items/{itemId}/jobScheduler", scheduleConfig)?.AsObject() ?? [];

    public JsonNode? UpdateItemSchedule(string workspaceId, string itemId, JsonNode scheduleConfig) =>
        Request("PATCH", $"/workspaces/{workspaceId}/items/{itemId}/jobScheduler", scheduleConfig);

    public JsonNode? GetItemSchedule(string workspaceId, string itemId) =>
        Request("GET", $"/workspaces/{workspaceId}/items/{itemId}/jobScheduler");

    // -- capacity (ARM) ------------------------------------------------------

    public JsonObject? ResumeCapacity(string subscriptionId, string resourceGroup, string capacityName) =>
        ArmCapacityAction(subscriptionId, resourceGroup, capacityName, "resume", "resuming");

    public JsonObject? PauseCapacity(string subscriptionId, string resourceGroup, string capacityName) =>
        ArmCapacityAction(subscriptionId, resourceGroup, capacityName, "suspend", "pausing");

    private JsonObject? ArmCapacityAction(string subscriptionId, string resourceGroup, string capacityName,
        string action, string resultStatus)
    {
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}" +
                  $"/providers/Microsoft.Fabric/capacities/{capacityName}/{action}?api-version=2023-11-01";
        try
        {
            var armToken = Auth.GetToken("https://management.azure.com/.default");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", armToken);
            using var resp = _http.Send(request);
            var status = (int)resp.StatusCode;
            return status is 200 or 202 ? new JsonObject { ["status"] = resultStatus } : null;
        }
        catch
        {
            return null;
        }
    }

    // -- utility -------------------------------------------------------------

    /// <summary>All items as a map of display_name -> {id, type, description}. Used by the planner.</summary>
    public Dictionary<string, Dictionary<string, object?>> GetWorkspaceItemsMap(string workspaceId)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var item in ListItems(workspaceId))
        {
            var name = item["displayName"]?.GetValue<string>() ?? "";
            result[name] = new Dictionary<string, object?>
            {
                ["id"] = item["id"]?.GetValue<string>(),
                ["type"] = item["type"]?.GetValue<string>(),
                ["description"] = item["description"]?.GetValue<string>(),
            };
        }
        return result;
    }

    // -- helpers -------------------------------------------------------------

    private static List<JsonNode> ValueList(JsonNode? result)
    {
        if (result?["value"] is JsonArray arr)
        {
            return arr.Where(n => n is not null).Select(n => n!).ToList();
        }
        return [];
    }

    private static JsonNode? SafeParse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildUrl(string url, IDictionary<string, string>? queryParams)
    {
        if (queryParams is null || queryParams.Count == 0)
        {
            return url;
        }
        var sep = url.Contains('?') ? '&' : '?';
        var query = string.Join("&", queryParams.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return url + sep + query;
    }
}
