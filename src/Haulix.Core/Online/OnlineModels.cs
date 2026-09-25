namespace Haulix.Core.Online;

// Data contract of the future HAULIX online service (see docs/online-api.md). HAULIX 0.0.x does not talk to any
// server: these types are used by the local sample backend and define what the real service will exchange.

/// <summary>The signed-in HAULIX account (login via Discord or Steam later).</summary>
public sealed record OnlineAccount(string Id, string Name, string? AvatarUrl, string Provider, string? VtcId, DateTime CreatedUtc);

/// <summary>A virtual trucking company.</summary>
public sealed record Vtc(string Id, string Name, string Tag, string? LogoUrl, string Language, string Region, string Game,
    int Members, bool Recruiting, string Description, long TotalKm, long Deliveries);

public enum VtcRole { Owner, Manager, Driver, Trainee }

public sealed record VtcMember(string AccountId, string Name, VtcRole Role, DateTime JoinedUtc, long Km, long Deliveries, double AvgScore, bool Online);

/// <summary>A job the VTC posted on its job board.</summary>
public sealed record VtcJob(string Id, string VtcId, string Cargo, string FromCity, string ToCity, double DistanceKm, long Reward,
    DateTime? DeadlineUtc, string? TakenBy, string Status);

/// <summary>A convoy or event.</summary>
public sealed record VtcEvent(string Id, string VtcId, string Title, DateTime StartUtc, string Server, string MeetingPoint,
    string Route, int Attendees, bool Public);

/// <summary>A live position shared with friends / the VTC (only when the player turns sharing on).</summary>
public sealed record LivePosition(string AccountId, string Name, double X, double Z, double HeadingDeg, double SpeedKmh,
    string? Cargo, string? DestinationCity, DateTime AtUtc);

/// <summary>A leaderboard row.</summary>
public sealed record LeaderboardEntry(int Rank, string AccountId, string Name, string? VtcTag, double Value);

/// <summary>What the online service currently can do. HAULIX shows it in Settings → Online.</summary>
public enum OnlineState
{
    /// <summary>This HAULIX version has no online service (the default of 0.0.x).</summary>
    NotAvailable,
    SignedOut,
    SigningIn,
    SignedIn,
    Offline,
}
