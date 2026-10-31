using Cronopus;

internal static class SchedulerStatusCases
{
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "cronopus-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbs = new DbConnectionService();
        dbs.Connect(new DbConfig { Type = "sqlite", SqlitePath = Path.Combine(root, "tasks.db") });
        var db = dbs.GetDb();
        SchedulerService.PrepareTables(db);
        using var scheduler = new SchedulerService(dbs);
        foreach (var exit in new[] { "", "0", "1" })
        {
            var id = "disabled_" + (exit == "" ? "unknown" : exit);
            db.InsertDic(new Dictionary<string, string>
            {
                ["id"] = id, ["name"] = id, ["executor"] = "cmd",
                ["script_path"] = "echo MUST-NOT-RUN", ["enabled"] = "false",
                ["schedule_mode"] = "off", ["status"] = "running",
                ["last_exit"] = exit, ["last_output"] = "saved output",
                ["last_run"] = "2026-10-20 14:46:17", ["runs_total"] = "227",
                ["runs_success"] = "45", ["max_threads"] = "4"
            }, "schedules");
        }
        try
        {
            db.InsertDic(new Dictionary<string, string>
            {
                ["id"] = "enabled", ["name"] = "enabled", ["executor"] = "cmd",
                ["script_path"] = "echo RECOVERED", ["enabled"] = "true",
                ["schedule_mode"] = "off", ["status"] = "running", ["max_threads"] = "1"
            }, "schedules");
            scheduler.Init();
            foreach (var suffix in new[] { "unknown", "0", "1" })
            {
                var id = "disabled_" + suffix;
                string Read(string field) => db.Get(field, "schedules", where: $"id='{id}'");
                if (Read("status") != "idle")
                    throw new Exception($"Disabled task {id} still shows Running after startup with no instances");
                if (scheduler.IsRunning(id) || scheduler.GetInstances(id).Count != 0)
                    throw new Exception("Disabled task was restarted");
                if (Read("last_output") != "saved output" || Read("runs_total") != "227"
                    || Read("runs_success") != "45" || Read("last_run") != "2026-10-20 14:46:17"
                    || Read("last_exit") != (suffix == "unknown" ? "" : suffix))
                    throw new Exception("Startup repair changed execution history");
            }
            Console.WriteLine("PASS disabled tasks lose stale Running at startup without restarting or changing history");
            for (var i = 0; i < 100; i++)
            {
                if (db.Get("runs_total", "schedules", where: "id='enabled'") == "1") break;
                await Task.Delay(100);
            }
            if (db.Get("status", "schedules", where: "id='enabled'") != "idle"
                || db.Get("runs_total", "schedules", where: "id='enabled'") != "1"
                || !db.Get("last_output", "schedules", where: "id='enabled'").Contains("RECOVERED"))
                throw new Exception("Enabled task was not recovered and completed");
            Console.WriteLine("PASS enabled task still restarts and completes after startup recovery");
        }
        finally
        {
            scheduler.Dispose();
            dbs.Disconnect();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }
}
