using System.IO.Compression;
using System.Text.Json;
using Haulix.Core.Data;
using Haulix.Core.Settings;
using Microsoft.Data.Sqlite;

namespace Haulix.Core.Services;

public sealed record BackupInfo(string Name, string Path, DateTime CreatedUtc, long SizeBytes, string Kind);

/// <summary>Export/import of the HAULIX database as <c>.haulix</c> archives, plus rolling automatic backups.</summary>
public sealed class BackupService(Database db, SettingsStore settings, string defaultBackupFolder)
{
    public const string Extension = ".haulix";

    public string BackupFolder => settings.Load().Data.BackupFolder is { Length: > 0 } f ? f : defaultBackupFolder;

    public BackupInfo Export(string destination, string kind = "manual")
    {
        var temp = Path.Combine(Path.GetTempPath(), $"haulix-export-{Guid.NewGuid():N}.db");
        try
        {
            db.BackupTo(temp);
            SqliteConnection.ClearAllPools();
            if (File.Exists(destination)) File.Delete(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var zip = ZipFile.Open(destination, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(temp, "haulix.db", CompressionLevel.Optimal);
                var manifest = zip.CreateEntry("manifest.json");
                using var w = new StreamWriter(manifest.Open());
                w.Write(JsonSerializer.Serialize(new
                {
                    app = "HAULIX ETS2 Logger",
                    format = 1,
                    schema = Database.SchemaVersion,
                    createdUtc = DateTime.UtcNow,
                    kind,
                    machine = Environment.MachineName,
                }));
            }
            var fi = new FileInfo(destination);
            return new BackupInfo(fi.Name, fi.FullName, DateTime.UtcNow, fi.Length, kind);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>Replaces the live database with the archive's contents. A safety backup is taken first.</summary>
    public void Import(string archive)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"haulix-import-{Guid.NewGuid():N}.db");
        try
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                var entry = zip.GetEntry("haulix.db") ?? throw new InvalidDataException("Not a HAULIX backup: haulix.db missing.");
                entry.ExtractToFile(temp, true);
            }
            using (var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                check.Open();
                if (Database.Scalar<string>(check, "PRAGMA quick_check;") != "ok") throw new InvalidDataException("Backup database is damaged.");
                var version = Database.Scalar<long>(check, "PRAGMA user_version;");
                if (version > Database.SchemaVersion) throw new InvalidDataException("Backup was made by a newer HAULIX version.");
                if (Database.Scalar<long>(check, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'deliveries'") == 0)
                    throw new InvalidDataException("Backup does not contain HAULIX data.");
            }

            Export(Path.Combine(BackupFolder, $"before-import-{DateTime.Now:yyyyMMdd-HHmmss}{Extension}"), "safety");

            using (var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
            using (var dst = db.Open())
            {
                src.Open();
                src.BackupDatabase(dst);
            }
            db.Migrate();
            settings.Invalidate();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(temp);
        }
    }

    public BackupInfo? AutoBackupIfDue()
    {
        var s = settings.Load().Data;
        if (!s.AutoBackup) return null;
        var latest = List().Where(b => b.Kind == "auto").OrderByDescending(b => b.CreatedUtc).FirstOrDefault();
        if (latest is not null && DateTime.UtcNow - latest.CreatedUtc < TimeSpan.FromHours(Math.Max(1, s.BackupIntervalHours))) return null;
        var info = Export(Path.Combine(BackupFolder, $"auto-{DateTime.Now:yyyyMMdd-HHmmss}{Extension}"), "auto");
        Prune(s.BackupKeep);
        return info;
    }

    public List<BackupInfo> List()
    {
        if (!Directory.Exists(BackupFolder)) return new List<BackupInfo>();
        return new DirectoryInfo(BackupFolder).EnumerateFiles("*" + Extension)
            .Select(f => new BackupInfo(f.Name, f.FullName, f.LastWriteTimeUtc, f.Length,
                f.Name.StartsWith("auto-", StringComparison.Ordinal) ? "auto"
                : f.Name.StartsWith("before-import-", StringComparison.Ordinal) ? "safety" : "manual"))
            .OrderByDescending(b => b.CreatedUtc)
            .ToList();
    }

    private void Prune(int keep)
    {
        foreach (var old in List().Where(b => b.Kind == "auto").Skip(Math.Max(1, keep))) TryDelete(old.Path);
    }

    /// <summary>scope: history | cache | all</summary>
    public void Clear(string scope)
    {
        Export(Path.Combine(BackupFolder, $"before-clear-{DateTime.Now:yyyyMMdd-HHmmss}{Extension}"), "safety");
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        if (scope is "history" or "all")
        {
            foreach (var t in new[] { "route_points", "routes", "deliveries", "sessions", "events", "fleet_snapshots" })
                Database.Exec(c, $"DELETE FROM {t}");
        }
        if (scope is "cache" or "all")
        {
            Database.Exec(c, "DELETE FROM profile_cache");
            Database.Exec(c, "DELETE FROM cities");
            Database.Exec(c, "DELETE FROM cargo_names");
        }
        if (scope == "all") Database.Exec(c, "DELETE FROM settings");
        tx.Commit();
        Database.Exec(c, "VACUUM");
        settings.Invalidate();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
