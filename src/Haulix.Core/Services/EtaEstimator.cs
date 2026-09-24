using Haulix.Core.Telemetry;

namespace Haulix.Core.Services;

/// <summary>
/// Arrival estimate. <see cref="Source"/> is "game" (the in-game navigation's own estimate) or "haulix"
/// (HAULIX's road-map route with your recent average speed, used when the game has no GPS route).
/// Game time runs ≈19× faster than real time in ETS2; both are provided.
/// </summary>
public sealed record EtaInfo(
    string Source,
    double RemainingKm,
    double GameSeconds,
    double RealSeconds,
    double TimeScale,
    DateTime ArrivalUtc,
    uint ArrivalGameMinutes,
    int? DeadlineMarginGameMinutes,
    bool TargetsJob);

/// <summary>Turns navigation data into a stable real-time ETA (updated once per second, lightly smoothed).</summary>
public sealed class EtaEstimator
{
    private const double DefaultScale = 19;
    private const double DefaultSpeedKmh = 65;
    private double _avgKmh = DefaultSpeedKmh;
    private DateTime _lastUtc;
    private EtaInfo? _last;
    private double? _smoothReal;
    private string? _lastSource;
    private double _maxScale;

    /// <param name="preferRoute">True for a manual HAULIX destination: the in-game GPS points elsewhere then.</param>
    public EtaInfo? Update(TelemetrySnapshot s, double? routeRemainingKm, bool preferRoute = false)
    {
        var now = DateTime.UtcNow;
        var dt = _lastUtc == default ? 0 : (now - _lastUtc).TotalSeconds;
        if (dt is > 0 and < 0.9) return _last;
        _lastUtc = now;

        // Recent average moving speed (≈5 min window) for HAULIX's own estimate.
        var speed = Math.Abs(s.SpeedKmh);
        if (!s.Paused && speed > 5 && dt > 0) _avgKmh += (speed - _avgKmh) * Math.Min(1, dt / 300);

        // The local scale drops inside cities (e.g. 3); the remaining route is mostly open road, so use the
        // highest scale seen (≈19) instead of the current one, otherwise the ETA jumps when entering a city.
        if (s.TimeScale is > 0.5 and < 100) _maxScale = Math.Max(_maxScale, s.TimeScale);
        var scale = _maxScale > 0.5 ? Math.Max(_maxScale, 10) : DefaultScale;
        string source;
        double remainingKm, gameSeconds;
        if (!preferRoute && s.RouteTimeSeconds > 1 && s.RouteDistanceKm > 0.01)
        {
            source = "game";
            remainingKm = s.RouteDistanceKm;
            gameSeconds = s.RouteTimeSeconds;
        }
        else if (routeRemainingKm is > 0.05)
        {
            source = "haulix";
            remainingKm = routeRemainingKm.Value;
            gameSeconds = remainingKm / Math.Clamp(_avgKmh, 35, 85) * 3600;
        }
        else
        {
            _last = null; _smoothReal = null; _lastSource = null;
            return null;
        }

        var real = gameSeconds / scale;
        // Smooth small jitter, but follow big changes (reroute, source switch) immediately.
        if (_smoothReal is { } prev && _lastSource == source && Math.Abs(real - prev) < Math.Max(60, prev * 0.15))
            real = prev + (real - prev) * 0.3;
        _smoothReal = real;
        _lastSource = source;

        var arrivalGame = s.GameTimeMinutes + (uint)Math.Round(gameSeconds / 60);
        int? margin = !preferRoute && s.OnJob && s.JobDeadlineGameMinutes > 0 ? (int)s.JobDeadlineGameMinutes - (int)arrivalGame : null;
        _last = new EtaInfo(source, Math.Round(remainingKm, 2), Math.Round(gameSeconds), Math.Round(real), scale,
            now.AddSeconds(real), arrivalGame, margin, !preferRoute && s.OnJob);
        return _last;
    }

    public void Reset()
    {
        _last = null; _smoothReal = null; _lastSource = null;
    }
}
