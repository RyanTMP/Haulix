using Microsoft.Data.Sqlite;

namespace Haulix.Core.Data;

/// <summary>Local SQLite store. One connection per operation; WAL mode lets the UI read while telemetry writes.</summary>
public sealed class Database
{
    public const int SchemaVersion = 2;

    public Database(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public string Path { get; }
    public string ConnectionString { get; }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 4000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public void Migrate()
    {
        using var c = Open();
        Exec(c, "PRAGMA journal_mode = WAL;");
        var version = Scalar<long>(c, "PRAGMA user_version;");
        if (version >= SchemaVersion) return;

        using var tx = c.BeginTransaction();
        if (version < 1)
        {
            Exec(c, """
                CREATE TABLE IF NOT EXISTS settings (
                  key   TEXT PRIMARY KEY,
                  value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS deliveries (
                  id              INTEGER PRIMARY KEY AUTOINCREMENT,
                  source          TEXT NOT NULL,            -- 'telemetry' | 'save'
                  dedupe_key      TEXT NOT NULL UNIQUE,
                  profile_id      TEXT,
                  demo            INTEGER NOT NULL DEFAULT 0,
                  status          TEXT NOT NULL,            -- 'delivered' | 'cancelled'
                  started_utc     TEXT,
                  finished_utc    TEXT,
                  game_start_min  INTEGER,
                  game_end_min    INTEGER,
                  truck           TEXT,
                  truck_brand     TEXT,
                  truck_plate     TEXT,
                  trailer         TEXT,
                  driver          TEXT,
                  cargo           TEXT,
                  cargo_id        TEXT,
                  cargo_mass_kg   REAL,
                  origin_city     TEXT,
                  origin_city_id  TEXT,
                  origin_company  TEXT,
                  origin_country  TEXT,
                  dest_city       TEXT,
                  dest_city_id    TEXT,
                  dest_company    TEXT,
                  dest_country    TEXT,
                  planned_km      REAL,
                  distance_km     REAL,
                  income          INTEGER,
                  xp              INTEGER,
                  penalty         INTEGER,
                  fuel_used_l     REAL,
                  avg_speed_kmh   REAL,
                  max_speed_kmh   REAL,
                  drive_seconds   INTEGER,
                  game_minutes    INTEGER,
                  cargo_damage    REAL,
                  truck_damage    REAL,
                  autopark        INTEGER,
                  autoload        INTEGER,
                  market          TEXT,
                  special         INTEGER
                );
                CREATE INDEX IF NOT EXISTS ix_deliveries_finished ON deliveries(finished_utc);
                CREATE INDEX IF NOT EXISTS ix_deliveries_profile ON deliveries(profile_id);

                CREATE TABLE IF NOT EXISTS routes (
                  id           INTEGER PRIMARY KEY AUTOINCREMENT,
                  delivery_id  INTEGER REFERENCES deliveries(id) ON DELETE CASCADE,
                  profile_id   TEXT,
                  demo         INTEGER NOT NULL DEFAULT 0,
                  kind         TEXT NOT NULL,               -- 'job' | 'freeroam'
                  started_utc  TEXT NOT NULL,
                  ended_utc    TEXT,
                  distance_km  REAL NOT NULL DEFAULT 0,
                  origin_city  TEXT,
                  dest_city    TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_routes_delivery ON routes(delivery_id);

                CREATE TABLE IF NOT EXISTS route_points (
                  route_id  INTEGER NOT NULL REFERENCES routes(id) ON DELETE CASCADE,
                  seq       INTEGER NOT NULL,
                  x         REAL NOT NULL,
                  z         REAL NOT NULL,
                  speed     REAL NOT NULL,
                  t_utc     TEXT NOT NULL,
                  PRIMARY KEY (route_id, seq)
                ) WITHOUT ROWID;

                CREATE TABLE IF NOT EXISTS sessions (
                  id             INTEGER PRIMARY KEY AUTOINCREMENT,
                  profile_id     TEXT,
                  demo           INTEGER NOT NULL DEFAULT 0,
                  started_utc    TEXT NOT NULL,
                  ended_utc      TEXT,
                  distance_km    REAL NOT NULL DEFAULT 0,
                  drive_seconds  INTEGER NOT NULL DEFAULT 0,
                  idle_seconds   INTEGER NOT NULL DEFAULT 0,
                  max_speed_kmh  REAL NOT NULL DEFAULT 0,
                  fuel_used_l    REAL NOT NULL DEFAULT 0,
                  truck          TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_sessions_started ON sessions(started_utc);

                CREATE TABLE IF NOT EXISTS events (
                  id          INTEGER PRIMARY KEY AUTOINCREMENT,
                  at_utc      TEXT NOT NULL,
                  profile_id  TEXT,
                  demo        INTEGER NOT NULL DEFAULT 0,
                  type        TEXT NOT NULL,
                  amount      INTEGER,
                  detail      TEXT,
                  x           REAL,
                  z           REAL
                );
                CREATE INDEX IF NOT EXISTS ix_events_at ON events(at_utc);

                CREATE TABLE IF NOT EXISTS cities (
                  id        TEXT PRIMARY KEY,
                  name      TEXT NOT NULL,
                  country   TEXT,
                  x         REAL,
                  z         REAL,
                  samples   INTEGER NOT NULL DEFAULT 0,
                  last_utc  TEXT
                );

                CREATE TABLE IF NOT EXISTS cargo_names (
                  id    TEXT PRIMARY KEY,
                  name  TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS profile_cache (
                  profile_id  TEXT PRIMARY KEY,
                  parsed_utc  TEXT NOT NULL,
                  save_path   TEXT,
                  save_utc    TEXT,
                  json        TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS fleet_snapshots (
                  id          INTEGER PRIMARY KEY AUTOINCREMENT,
                  profile_id  TEXT NOT NULL,
                  taken_utc   TEXT NOT NULL,
                  money       INTEGER,
                  xp          INTEGER,
                  distance_km INTEGER,
                  trucks      INTEGER,
                  trailers    INTEGER,
                  garages     INTEGER,
                  drivers     INTEGER,
                  ai_revenue  INTEGER,
                  ai_profit   INTEGER,
                  json        TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_snap_profile ON fleet_snapshots(profile_id, taken_utc);
                """);
        }
        if (version < 2)
        {
            // 2: driving score per delivery + unlocked achievements.
            Exec(c, """
                ALTER TABLE deliveries ADD COLUMN score INTEGER;
                ALTER TABLE deliveries ADD COLUMN score_detail TEXT;
                CREATE TABLE IF NOT EXISTS achievements (
                  id           TEXT PRIMARY KEY,
                  unlocked_utc TEXT NOT NULL
                );
                """);
        }
        Exec(c, $"PRAGMA user_version = {SchemaVersion};");
        tx.Commit();
    }

    public static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static T Scalar<T>(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var r = cmd.ExecuteScalar();
        if (r is null or DBNull) return default!;
        return (T)Convert.ChangeType(r, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Runs a query and returns rows as dictionaries (column name → value), ready for JSON.</summary>
    public static List<Dictionary<string, object?>> Rows(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        var list = new List<Dictionary<string, object?>>();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>(r.FieldCount);
            for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(row);
        }
        return list;
    }

    public long FileSize() => File.Exists(Path) ? new FileInfo(Path).Length : 0;

    /// <summary>Consistent online copy using SQLite's backup API.</summary>
    public void BackupTo(string destination)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
        if (File.Exists(destination)) File.Delete(destination);
        using var src = Open();
        using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        dst.Open();
        src.BackupDatabase(dst);
    }

    /// <summary>Checks integrity; returns null when OK, otherwise the problem.</summary>
    public string? Check()
    {
        try
        {
            using var c = Open();
            var result = Scalar<string>(c, "PRAGMA quick_check;");
            return result == "ok" ? null : result;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
