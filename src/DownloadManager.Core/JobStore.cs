using Microsoft.Data.Sqlite;

namespace DownloadManager.Core;

// Short-lived connections and serialized access keep this first version easy to reason about.
public sealed class JobStore
{
    private readonly string connectionString;
    private readonly object gate = new();
    public JobStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS jobs (
                id TEXT PRIMARY KEY, url TEXT NOT NULL, destination TEXT NOT NULL UNIQUE COLLATE NOCASE,
                category TEXT NOT NULL, state TEXT NOT NULL, bytes INTEGER NOT NULL DEFAULT 0,
                total INTEGER NULL, etag TEXT NULL, sha256 TEXT NULL, error TEXT NULL,
                created_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            );
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
            """;
        cmd.ExecuteNonQuery();
        // Additive migration preserves all prior jobs and partial-file checkpoints.
        using var transaction = db.BeginTransaction();
        using var schema = db.CreateCommand(); schema.Transaction = transaction;
        schema.CommandText = "PRAGMA table_info(jobs)";
        bool hasModified = false;
        using (var reader = schema.ExecuteReader())
            while (reader.Read()) if (reader.GetString(1) == "last_modified") hasModified = true;
        if (!hasModified) { schema.CommandText = "ALTER TABLE jobs ADD COLUMN last_modified TEXT NULL"; schema.ExecuteNonQuery(); }
        schema.CommandText = "PRAGMA user_version=2"; schema.ExecuteNonQuery();
        transaction.Commit();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return db;
    }
    public void Save(DownloadJob j)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO jobs(id,url,destination,category,state,bytes,total,etag,sha256,error,last_modified)
                VALUES($id,$url,$dest,$cat,$state,$bytes,$total,$etag,$hash,$error,$modified)
                ON CONFLICT(id) DO UPDATE SET state=excluded.state,bytes=excluded.bytes,total=excluded.total,
                etag=excluded.etag,sha256=excluded.sha256,error=excluded.error,last_modified=excluded.last_modified;
                """;
            cmd.Parameters.AddWithValue("$id", j.Id); cmd.Parameters.AddWithValue("$url", j.Url);
            cmd.Parameters.AddWithValue("$dest", j.Destination); cmd.Parameters.AddWithValue("$cat", j.Category);
            cmd.Parameters.AddWithValue("$state", j.State.ToString()); cmd.Parameters.AddWithValue("$bytes", j.Bytes);
            cmd.Parameters.AddWithValue("$total", (object?)j.Total ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$etag", (object?)j.ETag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hash", (object?)j.Sha256 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$error", (object?)j.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$modified", (object?)j.LastModified ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }
    public IReadOnlyList<DownloadJob> All()
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT id,url,destination,category,state,bytes,total,etag,sha256,error,last_modified FROM jobs ORDER BY created_utc,id";
            using var r = cmd.ExecuteReader(); var jobs = new List<DownloadJob>();
            while (r.Read()) jobs.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                Enum.Parse<JobState>(r.GetString(4)), r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10)));
            return jobs;
        }
    }
    public void DeletePending(string id)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM jobs WHERE id=$id AND state='BrowserPending' AND bytes=0";
            cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
        }
    }
    public DownloadJob Get(string id) => All().Single(j => j.Id == id);
    public int Setting(string key, int fallback)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key=$key"; cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() is long value ? (int)value : fallback;
        }
    }
    public void SetSetting(string key, int value)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$value", value); cmd.ExecuteNonQuery();
        }
    }
}
