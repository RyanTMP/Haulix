using System.Text.Json;

namespace Haulix.Core.Services;

/// <summary>
/// Driving score of a delivery, 0–100. Starts at 100 and loses points for speeding (share of driving time
/// above the limit + 5 km/h), cargo damage, new truck damage, fines and a late delivery.
/// </summary>
public sealed record DrivingScore(int Score, double SpeedingPct, double CargoDamagePct, double TruckDamagePct, int Fines, bool Late,
    int SpeedingPenalty, int CargoPenalty, int TruckPenalty, int FinePenalty, int LatePenalty)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DrivingScore Compute(double driveSeconds, double speedingSeconds, double cargoDamage, double truckDamageDelta, int fines, bool late)
    {
        var speedingPct = driveSeconds > 30 ? Math.Clamp(speedingSeconds / driveSeconds * 100, 0, 100) : 0;
        var cargoPct = Math.Clamp(cargoDamage * 100, 0, 100);
        var truckPct = Math.Clamp(truckDamageDelta * 100, 0, 100);
        var sp = (int)Math.Round(Math.Min(30, speedingPct * 0.6));
        var cp = (int)Math.Round(Math.Min(25, cargoPct * 2.5));
        var tp = (int)Math.Round(Math.Min(15, truckPct * 1.5));
        var fp = Math.Min(24, fines * 8);
        var lp = late ? 15 : 0;
        var score = Math.Clamp(100 - sp - cp - tp - fp - lp, 0, 100);
        return new DrivingScore(score, Math.Round(speedingPct, 1), Math.Round(cargoPct, 1), Math.Round(truckPct, 1), fines, late, sp, cp, tp, fp, lp);
    }
}
