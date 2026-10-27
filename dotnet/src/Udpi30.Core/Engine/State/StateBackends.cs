using System.Text.Json.Nodes;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Files.DataLake;

namespace Udpi30.Core.Engine.State;

/// <summary>Abstract state storage backend. Mirrors <c>engine/state_backend.py</c>.</summary>
public interface IStateBackend
{
    JsonObject? Read(string key);
    void Write(string key, JsonObject data);
    void Delete(string key);
    bool Exists(string key);
    List<string> ListKeys(string prefix = "");
    bool AcquireLock(string key, string owner, int timeoutSeconds = 1800);
    void ReleaseLock(string key);
    JsonObject? GetLockInfo(string key);
}

internal static class BackendUtil
{
    public static double EpochSeconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public static string Serialize(JsonObject data) =>
        data.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public static JsonObject? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

/// <summary>Local file-based state backend (default).</summary>
public sealed class LocalBackend : IStateBackend
{
    private readonly string _stateDir;

    public LocalBackend(string stateDir)
    {
        _stateDir = stateDir;
        Directory.CreateDirectory(_stateDir);
    }

    private string PathFor(string key) => Path.Combine(_stateDir, $"{key}.json");
    private string LockPath(string key) => Path.Combine(_stateDir, $"{key}.lock");

    public JsonObject? Read(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return BackendUtil.Parse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Write(string key, JsonObject data)
    {
        Directory.CreateDirectory(_stateDir);
        File.WriteAllText(PathFor(key), BackendUtil.Serialize(data));
    }

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool Exists(string key) => File.Exists(PathFor(key));

    public List<string> ListKeys(string prefix = "")
    {
        if (!Directory.Exists(_stateDir))
        {
            return [];
        }
        return Directory.GetFiles(_stateDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(k => k is not null && k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k!)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    public bool AcquireLock(string key, string owner, int timeoutSeconds = 1800)
    {
        var lockPath = LockPath(key);
        if (File.Exists(lockPath))
        {
            try
            {
                var lockData = BackendUtil.Parse(File.ReadAllText(lockPath));
                var ts = lockData?["timestamp"]?.GetValue<double>() ?? 0;
                if (BackendUtil.EpochSeconds() - ts <= timeoutSeconds)
                {
                    return false; // Active lock held.
                }
                // Otherwise stale — override.
            }
            catch (IOException)
            {
                // Ignore unreadable lock and override.
            }
        }

        var data = new JsonObject { ["owner"] = owner, ["timestamp"] = BackendUtil.EpochSeconds() };
        File.WriteAllText(lockPath, data.ToJsonString());
        return true;
    }

    public void ReleaseLock(string key)
    {
        var lockPath = LockPath(key);
        if (File.Exists(lockPath))
        {
            File.Delete(lockPath);
        }
    }

    public JsonObject? GetLockInfo(string key)
    {
        var lockPath = LockPath(key);
        if (!File.Exists(lockPath))
        {
            return null;
        }
        try
        {
            return BackendUtil.Parse(File.ReadAllText(lockPath));
        }
        catch (IOException)
        {
            return null;
        }
    }
}

/// <summary>Azure Blob Storage state backend with blob-lease locking.</summary>
public sealed class AzureBlobBackend : IStateBackend
{
    private readonly string _accountName;
    private readonly string _containerName;
    private readonly string? _accountKey;
    private readonly string _prefix;
    private BlobContainerClient? _client;
    private BlobLeaseClient? _lease;

    public AzureBlobBackend(IReadOnlyDictionary<string, string> config)
    {
        _accountName = config["account_name"];
        _containerName = config.GetValueOrDefault("container_name", "udpi30-state");
        _accountKey = config.GetValueOrDefault("account_key");
        _prefix = config.GetValueOrDefault("prefix", "");
    }

    private BlobContainerClient Container()
    {
        if (_client is not null)
        {
            return _client;
        }
        if (!string.IsNullOrEmpty(_accountKey))
        {
            var connStr = $"DefaultEndpointsProtocol=https;AccountName={_accountName};AccountKey={_accountKey};EndpointSuffix=core.windows.net";
            _client = new BlobContainerClient(connStr, _containerName);
        }
        else
        {
            var accountUrl = $"https://{_accountName}.blob.core.windows.net";
            _client = new BlobContainerClient(new Uri($"{accountUrl}/{_containerName}"), new DefaultAzureCredential());
        }
        try
        {
            _client.CreateIfNotExists();
        }
        catch
        {
            // Already exists / insufficient permission to create.
        }
        return _client;
    }

    private string BlobName(string key) => string.IsNullOrEmpty(_prefix) ? $"{key}.json" : $"{_prefix}/{key}.json";

    public JsonObject? Read(string key)
    {
        try
        {
            var blob = Container().GetBlobClient(BlobName(key));
            return BackendUtil.Parse(blob.DownloadContent().Value.Content.ToString());
        }
        catch
        {
            return null;
        }
    }

    public void Write(string key, JsonObject data)
    {
        var blob = Container().GetBlobClient(BlobName(key));
        blob.Upload(BinaryData.FromString(BackendUtil.Serialize(data)), overwrite: true);
    }

    public void Delete(string key)
    {
        try
        {
            Container().GetBlobClient(BlobName(key)).DeleteIfExists();
        }
        catch
        {
            // Best-effort.
        }
    }

    public bool Exists(string key)
    {
        try
        {
            return Container().GetBlobClient(BlobName(key)).Exists().Value;
        }
        catch
        {
            return false;
        }
    }

    public List<string> ListKeys(string prefix = "")
    {
        try
        {
            var search = string.IsNullOrEmpty(_prefix) ? prefix : $"{_prefix}/{prefix}";
            var keys = new List<string>();
            foreach (var blob in Container().GetBlobs(
                Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, search, default))
            {
                var name = blob.Name;
                if (name.EndsWith(".json", StringComparison.Ordinal) && !name.EndsWith(".lock", StringComparison.Ordinal))
                {
                    keys.Add(name.Split('/')[^1].Replace(".json", ""));
                }
            }
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
        catch
        {
            return [];
        }
    }

    public bool AcquireLock(string key, string owner, int timeoutSeconds = 1800)
    {
        try
        {
            var lockBlobName = BlobName(key).Replace(".json", ".lock");
            var blob = Container().GetBlobClient(lockBlobName);
            if (!blob.Exists().Value)
            {
                blob.Upload(BinaryData.FromString(
                    new JsonObject { ["owner"] = owner, ["timestamp"] = BackendUtil.EpochSeconds() }.ToJsonString()),
                    overwrite: true);
            }
            var leaseClient = blob.GetBlobLeaseClient();
            var lease = leaseClient.Acquire(TimeSpan.FromSeconds(60));
            _lease = leaseClient;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ReleaseLock(string key)
    {
        try
        {
            _lease?.Release();
            _lease = null;
        }
        catch
        {
            // Best-effort.
        }
    }

    public JsonObject? GetLockInfo(string key)
    {
        try
        {
            var lockBlobName = BlobName(key).Replace(".json", ".lock");
            var blob = Container().GetBlobClient(lockBlobName);
            var props = blob.GetProperties().Value;
            if (props.LeaseStatus == Azure.Storage.Blobs.Models.LeaseStatus.Locked)
            {
                return BackendUtil.Parse(blob.DownloadContent().Value.Content.ToString());
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Azure Data Lake Storage Gen2 state backend.</summary>
public sealed class AdlsBackend : IStateBackend
{
    private readonly string _accountName;
    private readonly string _filesystem;
    private readonly string _prefix;
    private DataLakeFileSystemClient? _client;
    private DataLakeLeaseClient? _lease;

    public AdlsBackend(IReadOnlyDictionary<string, string> config)
    {
        _accountName = config["account_name"];
        _filesystem = config.GetValueOrDefault("filesystem", "udpi30-state");
        _prefix = config.GetValueOrDefault("prefix", "");
    }

    private DataLakeFileSystemClient Fs()
    {
        if (_client is not null)
        {
            return _client;
        }
        var accountUrl = $"https://{_accountName}.dfs.core.windows.net";
        var service = new DataLakeServiceClient(new Uri(accountUrl), new DefaultAzureCredential());
        _client = service.GetFileSystemClient(_filesystem);
        try
        {
            _client.CreateIfNotExists();
        }
        catch
        {
            // Already exists.
        }
        return _client;
    }

    private string FilePath(string key) => string.IsNullOrEmpty(_prefix) ? $"{key}.json" : $"{_prefix}/{key}.json";

    public JsonObject? Read(string key) => DataLakeHelpers.Read(Fs(), FilePath(key));
    public void Write(string key, JsonObject data) => DataLakeHelpers.Write(Fs(), FilePath(key), data);
    public void Delete(string key) => DataLakeHelpers.Delete(Fs(), FilePath(key));
    public bool Exists(string key) => DataLakeHelpers.Exists(Fs(), FilePath(key));

    public List<string> ListKeys(string prefix = "")
    {
        var search = string.IsNullOrEmpty(_prefix) ? prefix : $"{_prefix}/{prefix}";
        return DataLakeHelpers.ListKeys(Fs(), search);
    }

    public bool AcquireLock(string key, string owner, int timeoutSeconds = 1800) =>
        DataLakeHelpers.AcquireLock(Fs(), FilePath(key).Replace(".json", ".lock"), owner, ref _lease);

    public void ReleaseLock(string key) => DataLakeHelpers.ReleaseLock(ref _lease);

    public JsonObject? GetLockInfo(string key) =>
        DataLakeHelpers.GetLockInfo(Fs(), FilePath(key).Replace(".json", ".lock"));
}

/// <summary>
/// OneLake (Fabric lakehouse) state backend — stores state files in a lakehouse's
/// Files section via the OneLake ADLS-compatible endpoint. Recommended for Fabric
/// projects so state lives alongside the data.
/// </summary>
public sealed class OneLakeBackend : IStateBackend
{
    private readonly string _workspaceId;
    private readonly string _lakehouseId;
    private readonly string _path;
    private DataLakeFileSystemClient? _client;
    private DataLakeLeaseClient? _lease;

    public OneLakeBackend(IReadOnlyDictionary<string, string> config)
    {
        _workspaceId = config["workspace_id"];
        _lakehouseId = config["lakehouse_id"];
        _path = config.GetValueOrDefault("path", ".udpi30-state");
    }

    private DataLakeFileSystemClient Fs()
    {
        if (_client is not null)
        {
            return _client;
        }
        var service = new DataLakeServiceClient(new Uri("https://onelake.dfs.fabric.microsoft.com"), new DefaultAzureCredential());
        _client = service.GetFileSystemClient(_workspaceId);
        return _client;
    }

    private string FilePath(string key) => $"{_lakehouseId}/Files/{_path}/{key}.json";

    public JsonObject? Read(string key) => DataLakeHelpers.Read(Fs(), FilePath(key));

    public void Write(string key, JsonObject data)
    {
        var fs = Fs();
        try
        {
            fs.GetDirectoryClient($"{_lakehouseId}/Files/{_path}").CreateIfNotExists();
        }
        catch
        {
            // Already exists.
        }
        DataLakeHelpers.Write(fs, FilePath(key), data);
    }

    public void Delete(string key) => DataLakeHelpers.Delete(Fs(), FilePath(key));
    public bool Exists(string key) => DataLakeHelpers.Exists(Fs(), FilePath(key));

    public List<string> ListKeys(string prefix = "") =>
        DataLakeHelpers.ListKeys(Fs(), $"{_lakehouseId}/Files/{_path}", prefix);

    public bool AcquireLock(string key, string owner, int timeoutSeconds = 1800) =>
        DataLakeHelpers.AcquireLock(Fs(), FilePath(key).Replace(".json", ".lock"), owner, ref _lease);

    public void ReleaseLock(string key) => DataLakeHelpers.ReleaseLock(ref _lease);

    public JsonObject? GetLockInfo(string key) =>
        DataLakeHelpers.GetLockInfo(Fs(), FilePath(key).Replace(".json", ".lock"));
}

/// <summary>Shared Data Lake (ADLS Gen2 / OneLake) operations.</summary>
internal static class DataLakeHelpers
{
    public static JsonObject? Read(DataLakeFileSystemClient fs, string path)
    {
        try
        {
            var file = fs.GetFileClient(path);
            using var stream = file.OpenRead();
            using var reader = new StreamReader(stream);
            return BackendUtil.Parse(reader.ReadToEnd());
        }
        catch
        {
            return null;
        }
    }

    public static void Write(DataLakeFileSystemClient fs, string path, JsonObject data)
    {
        var file = fs.GetFileClient(path);
        file.Upload(BinaryData.FromString(BackendUtil.Serialize(data)).ToStream(), overwrite: true);
    }

    public static void Delete(DataLakeFileSystemClient fs, string path)
    {
        try
        {
            fs.GetFileClient(path).DeleteIfExists();
        }
        catch
        {
            // Best-effort.
        }
    }

    public static bool Exists(DataLakeFileSystemClient fs, string path)
    {
        try
        {
            return fs.GetFileClient(path).Exists().Value;
        }
        catch
        {
            return false;
        }
    }

    public static List<string> ListKeys(DataLakeFileSystemClient fs, string searchPath, string prefix = "")
    {
        try
        {
            var keys = new List<string>();
            foreach (var path in fs.GetPaths(new Azure.Storage.Files.DataLake.Models.DataLakeGetPathsOptions { Path = searchPath, Recursive = false }))
            {
                var name = (path.Name ?? "").Split('/')[^1];
                if (name.EndsWith(".json", StringComparison.Ordinal) && !name.EndsWith(".lock", StringComparison.Ordinal))
                {
                    var key = name.Replace(".json", "");
                    if (key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        keys.Add(key);
                    }
                }
            }
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
        catch
        {
            return [];
        }
    }

    public static bool AcquireLock(DataLakeFileSystemClient fs, string lockPath, string owner, ref DataLakeLeaseClient? lease)
    {
        try
        {
            var file = fs.GetFileClient(lockPath);
            if (!file.Exists().Value)
            {
                file.Upload(BinaryData.FromString(
                    new JsonObject { ["owner"] = owner, ["timestamp"] = BackendUtil.EpochSeconds() }.ToJsonString()).ToStream(),
                    overwrite: true);
            }
            var leaseClient = file.GetDataLakeLeaseClient();
            leaseClient.Acquire(TimeSpan.FromSeconds(60));
            lease = leaseClient;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ReleaseLock(ref DataLakeLeaseClient? lease)
    {
        try
        {
            lease?.Release();
            lease = null;
        }
        catch
        {
            // Best-effort.
        }
    }

    public static JsonObject? GetLockInfo(DataLakeFileSystemClient fs, string lockPath)
    {
        try
        {
            var file = fs.GetFileClient(lockPath);
            var props = file.GetProperties().Value;
            if (props.LeaseStatus == Azure.Storage.Files.DataLake.Models.DataLakeLeaseStatus.Locked)
            {
                using var stream = file.OpenRead();
                using var reader = new StreamReader(stream);
                return BackendUtil.Parse(reader.ReadToEnd());
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Factory for state backends. Mirrors <c>create_backend</c>.</summary>
public static class StateBackendFactory
{
    public static IStateBackend Create(string backendType = "local",
        IReadOnlyDictionary<string, string>? config = null, string? projectDir = null)
    {
        config ??= new Dictionary<string, string>();

        switch (backendType)
        {
            case "local":
                var stateDir = Path.Combine(projectDir ?? Directory.GetCurrentDirectory(), ".udpi30");
                return new LocalBackend(stateDir);
            case "azureblob":
                if (!config.ContainsKey("account_name"))
                {
                    throw new ArgumentException("Azure Blob backend requires 'account_name' in config");
                }
                return new AzureBlobBackend(config);
            case "adls":
                if (!config.ContainsKey("account_name"))
                {
                    throw new ArgumentException("ADLS backend requires 'account_name' in config");
                }
                return new AdlsBackend(config);
            case "onelake":
                if (!config.ContainsKey("workspace_id") || !config.ContainsKey("lakehouse_id"))
                {
                    throw new ArgumentException("OneLake backend requires 'workspace_id' and 'lakehouse_id' in config");
                }
                return new OneLakeBackend(config);
            default:
                throw new ArgumentException($"Unknown state backend: {backendType}. Supported: local, azureblob, adls, onelake");
        }
    }
}
