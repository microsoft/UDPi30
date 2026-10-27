using Udpi30.Core.Engine;
using Udpi30.Core.Models;

namespace Udpi30.Core.Tests;

public class ConnectionCheckerTests
{
    private static (string host, int port)? Resolve(ConnectionType type, string? endpoint, string? connStr) =>
        ConnectionChecker.ResolveEndpoint(new ConnectionConfig { Type = type, Endpoint = endpoint }, connStr);

    [Fact]
    public void Sql_ConnectionString_With_Tcp_Prefix_And_Port()
    {
        var r = Resolve(ConnectionType.AzureSql, null,
            "Server=tcp:myserver.database.windows.net,1433;Database=db;User Id=u;Password=p;");
        Assert.Equal(("myserver.database.windows.net", 1433), r);
    }

    [Fact]
    public void Sql_Named_Instance_Strips_Instance_And_Uses_Default_Port()
    {
        var r = Resolve(ConnectionType.SqlServer, null, "Data Source=dbhost\\SQLEXPRESS;Initial Catalog=db;");
        Assert.Equal(("dbhost", 1433), r);
    }

    [Fact]
    public void Postgres_Style_Host_And_Explicit_Port_Key()
    {
        var r = Resolve(ConnectionType.Custom, null, "Host=pg.example.com;Port=5432;Database=app;");
        Assert.Equal(("pg.example.com", 5432), r);
    }

    [Fact]
    public void Cosmos_AccountEndpoint_Url()
    {
        var r = Resolve(ConnectionType.CosmosDb, null,
            "AccountEndpoint=https://acct.documents.azure.com:443/;AccountKey=secret==;");
        Assert.Equal(("acct.documents.azure.com", 443), r);
    }

    [Fact]
    public void Plain_Endpoint_Uses_Type_Default_Port()
    {
        var r = Resolve(ConnectionType.AzureSql, "myserver.database.windows.net", null);
        Assert.Equal(("myserver.database.windows.net", 1433), r);
    }

    [Fact]
    public void Url_Endpoint_Defaults_To_443_When_Default_Port()
    {
        var r = Resolve(ConnectionType.Kusto, "https://cluster.kusto.windows.net", null);
        Assert.Equal(("cluster.kusto.windows.net", 443), r);
    }

    [Fact]
    public void Url_Endpoint_Keeps_Explicit_Port()
    {
        var r = Resolve(ConnectionType.Http, "https://api.example.com:8443/health", null);
        Assert.Equal(("api.example.com", 8443), r);
    }

    [Fact]
    public void ConnectionString_Preferred_Over_Endpoint()
    {
        var r = Resolve(ConnectionType.SqlServer, "fallback.example.com", "Server=primary.example.com,1500;");
        Assert.Equal(("primary.example.com", 1500), r);
    }

    [Fact]
    public void Null_When_No_Host_In_ConnectionString()
    {
        var r = Resolve(ConnectionType.SqlServer, null, "Database=db;User Id=u;Password=p;");
        Assert.Null(r);
    }

    [Fact]
    public void Null_When_No_Source()
    {
        var r = Resolve(ConnectionType.Custom, null, null);
        Assert.Null(r);
    }

    [Fact]
    public void CheckAll_Marks_Untestable_Connection_As_Not_Tested()
    {
        var deployment = new DeploymentDefinition
        {
            Connections = { ["nosource"] = new ConnectionConfig { Type = ConnectionType.Custom } },
        };
        var results = ConnectionChecker.CheckAll(deployment, timeoutSeconds: 1);
        Assert.Single(results);
        Assert.False(results[0].Tested);
    }
}
