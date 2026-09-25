namespace Haulix.Core.Online;

/// <summary>
/// Entry point for everything online. In HAULIX 0.0.x the service is locked: <see cref="Available"/> is false,
/// no account exists and nothing is sent anywhere. The screens can use the local sample backend
/// (<see cref="UseSample"/>) to develop the VTC and Online areas against realistic data.
/// </summary>
public sealed class OnlineService
{
    /// <summary>Flip to true (with a real <see cref="IOnlineApi"/>) when the HAULIX server exists.</summary>
    public const bool Available = false;

    private IOnlineApi _api = new NoOnlineApi();

    public OnlineState State { get; private set; } = OnlineState.NotAvailable;
    public IOnlineApi Api => _api;

    /// <summary>Developer preview: local sample data for the online screens (no network).</summary>
    public bool UseSample
    {
        get => _api is SampleOnlineApi;
        set { _api = value ? new SampleOnlineApi() : new NoOnlineApi(); State = value ? OnlineState.SignedIn : OnlineState.NotAvailable; }
    }

    public object StatusPayload() => new
    {
        available = Available,
        state = State.ToString(),
        sample = UseSample,
        planned = new[] { "accounts", "vtc", "jobBoard", "events", "leaderboards", "cloudSync" },
    };
}
