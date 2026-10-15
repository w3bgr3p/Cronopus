using System.Text.Json;
using z3nDash;

static class PublishedFixture
{
    public static void Setup(string root)
    {
        foreach(var node in new[]{"local","remote"}){
            var dir=Path.Combine(root,node);Directory.CreateDirectory(dir);
            var config=new{DbConfig=new DbConfig{Type="sqlite",SqlitePath=Path.Combine(dir,"tasks.db")},LogsConfig=new LogsConfig{DashboardPort=node=="local"?"39217":"39221",LogsFolder=Path.Combine(dir,"logs"),ReportsFolder=Path.Combine(dir,"reports")}};
            File.WriteAllText(Path.Combine(dir,"appsettings.secrets.json"),JsonSerializer.Serialize(config));
            var db=new DbConnectionService();db.Connect(config.DbConfig);SchedulerService.PrepareTables(db.GetDb());
        }
        var remote=new BridgeStore(Path.Combine(root,"remote-state"));remote.State.Settings.Enabled=true;remote.State.Settings.BindAddress="127.0.0.1";remote.State.Settings.AdvertisedAddress="127.0.0.1";remote.State.Settings.Port=39219;
        var key=remote.CreateKey("published-e2e");using var cert=remote.Certificate();remote.Save();
        var local=new BridgeStore(Path.Combine(root,"local-state"));
        var connection=new BridgeConnection{Name="Published Server",Address="https://127.0.0.1:39219",NodeId=remote.NodeId,Fingerprint=BridgeStore.Fingerprint(cert),Secret=key.Secret};
        local.State.Connections.Add(connection);local.Save();
        File.WriteAllText(Path.Combine(root,"test.py"),"print('published bridge')");
        File.WriteAllText(Path.Combine(root,"fixture.json"),JsonSerializer.Serialize(new{root,local_url="http://localhost:39217",remote_url=connection.Address,connection_id=connection.Id}));
        Console.WriteLine("Published fixture prepared");
    }
}
