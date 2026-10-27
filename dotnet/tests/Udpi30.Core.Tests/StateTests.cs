using System.Text.Json;
using Udpi30.Core.Engine.State;

namespace Udpi30.Core.Tests;

public class StateTests
{
    private static string TempProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "udp-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void RecordDeployment_Then_Load_RoundTrips()
    {
        var dir = TempProject();
        try
        {
            var mgr = new StateManager(dir, "dev");
            var deployed = new Dictionary<string, Dictionary<string, object?>>
            {
                ["bronze"] = new() { ["id"] = "lh-1", ["type"] = "Lakehouse", ["definition_hash"] = "abc123" },
            };
            mgr.RecordDeployment("medallion", "1.0.0", "ws-1", "Dev", deployed);

            var loaded = new StateManager(dir, "dev").Load();
            Assert.Equal("medallion", loaded.DeploymentName);
            Assert.Equal("ws-1", loaded.WorkspaceId);
            Assert.True(loaded.Resources.ContainsKey("bronze"));
            Assert.Equal("lh-1", loaded.Resources["bronze"].ItemId);
            Assert.Equal("abc123", loaded.Resources["bronze"].DefinitionHash);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void State_File_Is_SnakeCase_Compatible()
    {
        var dir = TempProject();
        try
        {
            var mgr = new StateManager(dir, "dev");
            mgr.RecordDeployment("p", "1.0.0", "ws", "Dev",
                new Dictionary<string, Dictionary<string, object?>>
                {
                    ["x"] = new() { ["id"] = "i", ["type"] = "Notebook" },
                });

            var json = File.ReadAllText(Path.Combine(dir, ".udpi30", "state-dev.json"));
            using var doc = JsonDocument.Parse(json);
            // Python-compatible snake_case keys.
            Assert.True(doc.RootElement.TryGetProperty("deployment_name", out _));
            Assert.True(doc.RootElement.TryGetProperty("workspace_id", out _));
            Assert.True(doc.RootElement.TryGetProperty("last_deployed", out _));
            var res = doc.RootElement.GetProperty("resources").GetProperty("x");
            Assert.True(res.TryGetProperty("item_id", out _));
            Assert.True(res.TryGetProperty("item_type", out _));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DetectDrift_Reports_Added_Removed_Modified()
    {
        var dir = TempProject();
        try
        {
            var mgr = new StateManager(dir, "dev");
            mgr.RecordDeployment("p", "1.0.0", "ws", "Dev",
                new Dictionary<string, Dictionary<string, object?>>
                {
                    ["keep"] = new() { ["id"] = "id-keep", ["type"] = "Lakehouse" },
                    ["changed"] = new() { ["id"] = "id-old", ["type"] = "Notebook" },
                    ["gone"] = new() { ["id"] = "id-gone", ["type"] = "Notebook" },
                });

            var live = new Dictionary<string, Dictionary<string, object?>>
            {
                ["keep"] = new() { ["id"] = "id-keep" },
                ["changed"] = new() { ["id"] = "id-new" },     // modified
                ["brand_new"] = new() { ["id"] = "id-new2" },  // added
            };

            var drift = mgr.DetectDrift(live);
            Assert.Equal("added", drift["brand_new"]);
            Assert.Equal("removed", drift["gone"]);
            Assert.Equal("modified", drift["changed"]);
            Assert.False(drift.ContainsKey("keep"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Local_Lock_Blocks_Second_Acquire()
    {
        var dir = TempProject();
        try
        {
            var backend = new LocalBackend(Path.Combine(dir, ".udpi30"));
            Assert.True(backend.AcquireLock("lock-dev", "owner-a"));
            Assert.False(backend.AcquireLock("lock-dev", "owner-b"));
            backend.ReleaseLock("lock-dev");
            Assert.True(backend.AcquireLock("lock-dev", "owner-b"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
