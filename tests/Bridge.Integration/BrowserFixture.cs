using System.Text.Json;
using Cronopus;

static class BrowserFixture
{
    public static async Task Serve(string root,BridgeStore remoteStore,Uri remoteUri,string secret)
    {
        var localDb=new DbConnectionService();localDb.Connect(new DbConfig{Type="sqlite",SqlitePath=Path.Combine(root,"local.db")});
        SchedulerService.PrepareTables(localDb.GetDb());using var scheduler=new SchedulerService(localDb);
        var server=new EmbeddedServer(new LogsConfig{DashboardPort="39187",LogsFolder=Path.Combine(root,"logs"),ReportsFolder=Path.Combine(root,"reports")},localDb);
        var tasker=new TaskerService(localDb,scheduler,server.WwwrootPath);
        var store=new BridgeStore(Path.Combine(root,"local-bridge"));var host=new BridgeHost(store,tasker,scheduler);
        using var certificate=remoteStore.Certificate();
        var connection=new BridgeConnection{Name="Fixture Server",Address=remoteUri.ToString(),NodeId=remoteStore.NodeId,Fingerprint=BridgeStore.Fingerprint(certificate),Secret=secret};
        store.State.Connections.Add(connection);store.Save();
        server.RegisterHandler(new SchedulerHandler(tasker));server.RegisterHandler(new BridgeLocalHandler(store,host));server.Start();
        await tasker.InvokeAsync("POST","/tasker/save",JsonSerializer.SerializeToElement(new{id="local-only",name="LOCAL SENTINEL",enabled="false",schedule_mode="off",executor="python",script_path=Path.Combine(root,"test.py")}),CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root,"test.py"),"print('bridge fixture')");
        var info=new{local_url=$"http://localhost:{server.Port}",remote_url=remoteUri.ToString(),connection_id=connection.Id,root};
        var path=Path.Combine(Directory.GetCurrentDirectory(),".superpowers","sdd","2026-10-15-tasker-remote-bridge","fixture.json");
        File.WriteAllText(path,JsonSerializer.Serialize(info));Console.WriteLine("BROWSER FIXTURE READY "+info.local_url);
        await Task.Delay(Timeout.Infinite);
    }
}
