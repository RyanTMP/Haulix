using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Haulix.Core;
using Haulix.Core.Telemetry;

namespace Haulix.App;

/// <summary>
/// Discord Rich Presence over Discord's local IPC pipe (no server, no Discord SDK): shows the current drive
/// on the player's Discord profile, e.g. "Steel coils · Hamburg → Prague" / "120 km left · arrival in 23 min".
/// Needs the Application ID of a Discord app (Settings → General); clears the status when ETS2 closes.
/// </summary>
internal sealed class DiscordPresence : IDisposable
{
    private readonly HaulixEngine _engine;
    private readonly System.Threading.Timer _timer;
    private NamedPipeClientStream? _pipe;
    private string? _connectedFor;
    private string? _lastPayload;
    private DateTime _sessionStartUtc;
    private int _busy;

    public DiscordPresence(HaulixEngine engine)
    {
        _engine = engine;
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var g = _engine.Settings.Load().General;
            var s = _engine.LastSnapshot;
            var live = s is not null && (DateTime.UtcNow - s.CapturedUtc).TotalSeconds < 30;
            if (!g.DiscordPresence || string.IsNullOrWhiteSpace(g.DiscordAppId) || !live)
            {
                if (_pipe is not null) { SetActivity(null); Disconnect(); }
                _sessionStartUtc = default;
                return;
            }
            if (_connectedFor != g.DiscordAppId) { Disconnect(); if (!Connect(g.DiscordAppId.Trim())) return; }
            if (_sessionStartUtc == default) _sessionStartUtc = DateTime.UtcNow;
            SetActivity(Activity(s!, _engine.Notifier.German));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Discord presence failed: {ex.Message}");
            Disconnect();
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private object Activity(TelemetrySnapshot s, bool de)
    {
        string details, state;
        if (s.OnJob && !string.IsNullOrEmpty(s.DestinationCity))
        {
            details = $"{s.Cargo} · {s.SourceCity} → {s.DestinationCity}";
            state = s.Eta is { } e
                ? (de ? $"Noch {e.RemainingKm:0} km · Ankunft in {Minutes(e.RealSeconds)}" : $"{e.RemainingKm:0} km left · arrival in {Minutes(e.RealSeconds)}")
                : (de ? "Unterwegs" : "On the road");
        }
        else
        {
            details = de ? "Freie Fahrt" : "Free roam";
            state = $"{s.TruckBrand} {s.TruckName}".Trim();
        }
        return new
        {
            details = Clip(details), state = Clip(state),
            timestamps = new { start = new DateTimeOffset(_sessionStartUtc).ToUnixTimeSeconds() },
            assets = new { large_image = "haulix", large_text = "HAULIX · ETS2 Logger", small_image = "truck", small_text = Clip($"{s.TruckBrand} {s.TruckName}".Trim()) },
        };
    }

    private static string Minutes(double seconds)
    {
        var m = (int)Math.Round(seconds / 60);
        return m < 60 ? $"{Math.Max(1, m)} min" : $"{m / 60} h {m % 60:00} min";
    }

    private static string Clip(string s) => s.Length <= 2 ? s.PadRight(2) : s.Length > 120 ? s[..120] : s;

    private bool Connect(string appId)
    {
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.None);
            try
            {
                pipe.Connect(300);
                _pipe = pipe;
                Write(0, new { v = 1, client_id = appId });
                Read(); // READY (or an error frame for an unknown app id)
                _connectedFor = appId;
                _lastPayload = null;
                return true;
            }
            catch (Exception)
            {
                pipe.Dispose();
                _pipe = null;
            }
        }
        return false; // Discord not running
    }

    private void SetActivity(object? activity)
    {
        if (_pipe is null) return;
        var payload = JsonSerializer.Serialize(new { cmd = "SET_ACTIVITY", args = new { pid = Environment.ProcessId, activity }, nonce = Guid.NewGuid().ToString() });
        var key = JsonSerializer.Serialize(activity);
        if (key == _lastPayload) return;
        WriteRaw(1, payload);
        Read();
        _lastPayload = key;
    }

    private void Write(int op, object body) => WriteRaw(op, JsonSerializer.Serialize(body));

    private void WriteRaw(int op, string json)
    {
        var data = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), data.Length);
        data.CopyTo(frame, 8);
        _pipe!.Write(frame);
        _pipe.Flush();
    }

    private void Read()
    {
        var header = new byte[8];
        ReadExactly(header);
        var len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (len is > 0 and < 1 << 20) ReadExactly(new byte[len]);
    }

    private void ReadExactly(byte[] buf)
    {
        var read = 0;
        while (read < buf.Length)
        {
            var n = _pipe!.Read(buf, read, buf.Length - read);
            if (n <= 0) throw new IOException("Discord closed the connection");
            read += n;
        }
    }

    private void Disconnect()
    {
        try { _pipe?.Dispose(); } catch (Exception) { }
        _pipe = null;
        _connectedFor = null;
        _lastPayload = null;
    }

    public void Dispose()
    {
        _timer.Dispose();
        try { if (_pipe is not null) SetActivity(null); } catch (Exception) { }
        Disconnect();
    }
}
