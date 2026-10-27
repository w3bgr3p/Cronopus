using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Cronopus;

internal sealed class PostgresViewerHandler
{
    private const int MaxRows = 10000;
    private sealed record Request(string ConnectionString = "", string Sql = "");

    public bool Matches(string path) => path.StartsWith("/postgres-viewer/");

    public async Task Handle(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath.ToLowerInvariant();
        if (ctx.Request.HttpMethod != "POST" ||
            (path != "/postgres-viewer/tables" && path != "/postgres-viewer/query"))
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        string? password = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var token = timeout.Token;
            var request = await JsonSerializer.DeserializeAsync<Request>(ctx.Request.InputStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, token);
            if (string.IsNullOrWhiteSpace(request?.ConnectionString))
            {
                await Error(ctx, "Enter a PostgreSQL connection string.");
                return;
            }

            var query = path == "/postgres-viewer/query";
            var sql = request.Sql?.Trim().TrimEnd(';').TrimEnd() ?? "";
            if (query && !Regex.IsMatch(sql, @"\A(?:SELECT|WITH)\b", RegexOptions.IgnoreCase))
            {
                await Error(ctx, "Only SELECT or WITH queries are allowed.");
                return;
            }
            var settings = new NpgsqlConnectionStringBuilder(request.ConnectionString)
            {
                Pooling = false,
                Multiplexing = false,
                Enlist = false,
                Timeout = 10,
                CommandTimeout = 30,
                IncludeErrorDetail = false,
                PersistSecurityInfo = false,
                ApplicationName = "Cronopus SQL viewer"
            };
            password = settings.Password;
            await using var conn = new NpgsqlConnection(settings.ConnectionString);
            await conn.OpenAsync(token);
            await using var transaction = await conn.BeginTransactionAsync(token);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, transaction))
                await readOnly.ExecuteNonQueryAsync(token);

            if (!query)
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT n.nspname, c.relname
                    FROM pg_catalog.pg_class c
                    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                    WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f')
                      AND n.nspname <> 'information_schema' AND n.nspname !~ '^pg_'
                      AND has_schema_privilege(n.oid, 'USAGE')
                      AND has_table_privilege(c.oid, 'SELECT')
                    ORDER BY n.nspname, c.relname
                    """, conn, transaction);
                var tables = new List<object>();
                await using (var reader = await cmd.ExecuteReaderAsync(token))
                    while (await reader.ReadAsync(token))
                        tables.Add(new { schema = reader.GetString(0), name = reader.GetString(1) });
                await HttpHelpers.WriteJson(ctx.Response, new
                {
                    tables,
                    database = conn.Database,
                    host = conn.Host,
                    port = conn.Port
                });
                return;
            }

            // The positional limit parameter sends SQL through PostgreSQL's single-statement
            // Parse protocol. A statement batch cannot end our read-only transaction.
            // The subquery also rejects commands and data-modifying CTEs.
            await using var command = new NpgsqlCommand(
                $"SELECT * FROM (\n{sql}\n) AS cronopus_result LIMIT $1", conn, transaction)
            { AllResultTypesAreUnknown = true };
            command.Parameters.Add(new NpgsqlParameter { Value = MaxRows + 1 });
            var columns = new List<string>();
            var rows = new List<List<string?>>();
            bool truncated = false;
            await using (var reader = await command.ExecuteReaderAsync(token))
            {
                for (int i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));
                while (await reader.ReadAsync(token))
                {
                    if (rows.Count == MaxRows) { truncated = true; break; }
                    var row = new List<string?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row.Add(reader.IsDBNull(i) ? null : reader.GetString(i));
                    rows.Add(row);
                }
            }
            await HttpHelpers.WriteJson(ctx.Response, new
            {
                columns, rows, rowids = Array.Empty<long>(), table = "", truncated, limit = MaxRows
            });
        }
        catch (PostgresException ex)
        {
            var message = ex.MessageText;
            if (!string.IsNullOrEmpty(password)) message = message.Replace(password, "***", StringComparison.Ordinal);
            await Error(ctx, $"PostgreSQL ({ex.SqlState}): {message}");
        }
        catch (OperationCanceledException) { await Error(ctx, "PostgreSQL request timed out."); }
        catch (NpgsqlException) { await Error(ctx, "Could not connect to PostgreSQL or complete the query. Check host, port, database, credentials and SSL settings."); }
        catch (ArgumentException) { await Error(ctx, "Invalid PostgreSQL connection string. Use Host=...;Port=5432;Database=...;Username=...;Password=..."); }
        catch (JsonException) { await Error(ctx, "Invalid request body."); }
        catch (Exception) { await Error(ctx, "Could not complete the PostgreSQL request."); }
    }

    private static async Task Error(HttpListenerContext ctx, string message)
    {
        ctx.Response.StatusCode = 400;
        await HttpHelpers.WriteJson(ctx.Response, new { error = message });
    }
}
