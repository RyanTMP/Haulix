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

    /// <summary>
    /// Version of the HAULIX License Agreement (Part B – online services) and Privacy Policy. Signing in requires the
    /// user to have accepted this version; raise it when the terms change so everyone is asked again.
    /// </summary>
    public const string TermsVersion = "2.0";

    public static bool TermsAccepted(Settings.OnlineSettings s) => s.AcceptedTermsVersion == TermsVersion;

    private IOnlineApi _api = new NoOnlineApi();

    public OnlineState State { get; private set; } = OnlineState.NotAvailable;
    public IOnlineApi Api => _api;

    /// <summary>Developer preview: local sample data for the online screens (no network).</summary>
    public bool UseSample
    {
        get => _api is SampleOnlineApi;
        set { _api = value ? new SampleOnlineApi() : new NoOnlineApi(); State = value ? OnlineState.SignedIn : OnlineState.NotAvailable; }
    }

    public object StatusPayload(Settings.OnlineSettings s) => new
    {
        available = Available,
        termsVersion = TermsVersion,
        termsAccepted = TermsAccepted(s),
        termsAcceptedUtc = s.AcceptedTermsUtc,
        state = State.ToString(),
        sample = UseSample,
        planned = new[] { "accounts", "vtc", "jobBoard", "events", "leaderboards", "cloudSync" },
    };
}
