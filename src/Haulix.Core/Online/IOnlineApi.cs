using Haulix.Core.Data;

namespace Haulix.Core.Online;

/// <summary>
/// Client side of the HAULIX online service. One interface for everything the VTC / Online areas need, so the
/// UI can be built against sample data now and against the real server later without changes.
/// Endpoints and payloads are described in docs/online-api.md.
/// </summary>
public interface IOnlineApi
{
    /// <summary>True when this implementation actually talks to a server.</summary>
    bool IsRemote { get; }

    Task<OnlineAccount?> MeAsync(CancellationToken ct = default);

    // VTC
    Task<IReadOnlyList<Vtc>> SearchVtcsAsync(string? query, string? language, string? region, CancellationToken ct = default);
    Task<Vtc?> GetVtcAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<VtcMember>> GetMembersAsync(string vtcId, CancellationToken ct = default);
    Task RequestJoinAsync(string vtcId, string message, CancellationToken ct = default);
    Task<IReadOnlyList<VtcJob>> GetJobsAsync(string vtcId, CancellationToken ct = default);
    Task<IReadOnlyList<VtcEvent>> GetEventsAsync(string? vtcId, CancellationToken ct = default);
    Task<IReadOnlyList<LeaderboardEntry>> GetLeaderboardAsync(string metric, string period, string? vtcId, CancellationToken ct = default);

    // Cloud sync (deliveries are identified by their dedupe key, so re-uploads are harmless)
    Task<int> UploadDeliveriesAsync(IReadOnlyList<Dictionary<string, object?>> deliveries, CancellationToken ct = default);
}

/// <summary>The implementation in HAULIX 0.0.x: no server – every call reports that online features are not available.</summary>
public sealed class NoOnlineApi : IOnlineApi
{
    public bool IsRemote => false;
    private static NotSupportedException Na() => new("Online features are not available in this HAULIX version.");
    public Task<OnlineAccount?> MeAsync(CancellationToken ct = default) => Task.FromResult<OnlineAccount?>(null);
    public Task<IReadOnlyList<Vtc>> SearchVtcsAsync(string? q, string? l, string? r, CancellationToken ct = default) => throw Na();
    public Task<Vtc?> GetVtcAsync(string id, CancellationToken ct = default) => throw Na();
    public Task<IReadOnlyList<VtcMember>> GetMembersAsync(string vtcId, CancellationToken ct = default) => throw Na();
    public Task RequestJoinAsync(string vtcId, string message, CancellationToken ct = default) => throw Na();
    public Task<IReadOnlyList<VtcJob>> GetJobsAsync(string vtcId, CancellationToken ct = default) => throw Na();
    public Task<IReadOnlyList<VtcEvent>> GetEventsAsync(string? vtcId, CancellationToken ct = default) => throw Na();
    public Task<IReadOnlyList<LeaderboardEntry>> GetLeaderboardAsync(string m, string p, string? v, CancellationToken ct = default) => throw Na();
    public Task<int> UploadDeliveriesAsync(IReadOnlyList<Dictionary<string, object?>> d, CancellationToken ct = default) => throw Na();
}

/// <summary>
/// Local sample backend for building and testing the online screens (Settings → Online → developer preview).
/// Deterministic data, no network.
/// </summary>
public sealed class SampleOnlineApi : IOnlineApi
{
    public bool IsRemote => false;
    private static readonly DateTime Now = DateTime.UtcNow;

    private static readonly Vtc[] Vtcs =
    [
        new("vtc-nordlicht", "Nordlicht Logistik", "NLL", null, "de", "Europe", "ETS2", 48, true, "Relaxed German VTC, weekly convoys on Sunday evenings.", 2_840_000, 9_210),
        new("vtc-ironroad", "Iron Road Haulage", "IRH", null, "en", "Europe", "ETS2", 112, true, "Heavy haul and special transport, active on TruckersMP.", 7_120_000, 21_480),
        new("vtc-baltic", "Baltic Express", "BLX", null, "en", "Baltics", "ETS2", 23, false, "Small friendly VTC around the Baltic Sea.", 610_000, 1_930),
        new("vtc-alpen", "Alpen Transport", "APT", null, "de", "Alps", "ETS2", 31, true, "Mountain routes, realistic driving, 90 km/h.", 1_450_000, 4_880),
    ];

    public Task<OnlineAccount?> MeAsync(CancellationToken ct = default) =>
        Task.FromResult<OnlineAccount?>(new OnlineAccount("acc-demo", "Demo Driver", null, "sample", "vtc-nordlicht", Now.AddDays(-40)));

    public Task<IReadOnlyList<Vtc>> SearchVtcsAsync(string? query, string? language, string? region, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Vtc>>(Vtcs.Where(v =>
            (string.IsNullOrWhiteSpace(query) || v.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || v.Tag.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(language) || v.Language == language) && (string.IsNullOrEmpty(region) || v.Region == region)).ToList());

    public Task<Vtc?> GetVtcAsync(string id, CancellationToken ct = default) => Task.FromResult(Vtcs.FirstOrDefault(v => v.Id == id));

    public Task<IReadOnlyList<VtcMember>> GetMembersAsync(string vtcId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VtcMember>>(new[]
        {
            new VtcMember("acc-1", "Jonas", VtcRole.Owner, Now.AddDays(-400), 412_000, 1_320, 93.1, true),
            new VtcMember("acc-2", "Sofia", VtcRole.Manager, Now.AddDays(-260), 288_000, 910, 95.4, false),
            new VtcMember("acc-demo", "Demo Driver", VtcRole.Driver, Now.AddDays(-40), 38_400, 61, 88.7, true),
            new VtcMember("acc-4", "Marek", VtcRole.Trainee, Now.AddDays(-6), 2_100, 9, 81.0, false),
        });

    public Task RequestJoinAsync(string vtcId, string message, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<VtcJob>> GetJobsAsync(string vtcId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VtcJob>>(new[]
        {
            new VtcJob("job-1", vtcId, "Machine parts", "Hamburg", "Prague", 640, 12_000, Now.AddDays(2), null, "open"),
            new VtcJob("job-2", vtcId, "Frozen food", "Rotterdam", "Munich", 820, 15_500, Now.AddDays(1), "Sofia", "taken"),
            new VtcJob("job-3", vtcId, "Steel coils", "Linz", "Wrocław", 610, 11_200, null, null, "open"),
        });

    public Task<IReadOnlyList<VtcEvent>> GetEventsAsync(string? vtcId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VtcEvent>>(new[]
        {
            new VtcEvent("ev-1", "vtc-nordlicht", "Sunday convoy: Kiel → Venice", Now.Date.AddDays(3).AddHours(18), "TruckersMP Simulation 1", "Kiel, Posped", "Kiel – Hamburg – Munich – Venice", 34, true),
            new VtcEvent("ev-2", "vtc-nordlicht", "Training drive for new members", Now.Date.AddDays(6).AddHours(19), "TruckersMP Arcade", "Berlin, TradeAux", "Berlin – Dresden – Prague", 8, false),
        });

    public Task<IReadOnlyList<LeaderboardEntry>> GetLeaderboardAsync(string metric, string period, string? vtcId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LeaderboardEntry>>(new[]
        {
            new LeaderboardEntry(1, "acc-1", "Jonas", "NLL", 8_420), new LeaderboardEntry(2, "acc-2", "Sofia", "NLL", 7_910),
            new LeaderboardEntry(3, "acc-demo", "Demo Driver", "NLL", 5_130), new LeaderboardEntry(4, "acc-4", "Marek", "NLL", 1_020),
        });

    public Task<int> UploadDeliveriesAsync(IReadOnlyList<Dictionary<string, object?>> deliveries, CancellationToken ct = default) =>
        Task.FromResult(deliveries.Count);
}
