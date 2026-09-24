using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>Runs global AOB scans, then copies post-filtered addresses before releasing SDK-owned objects.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <see cref="AobScanRequest.Module" /> and <see cref="AobScanRequest.Range" /> are managed post-filters. Core
///         resolves the module first, then Cheat Engine runs one global <c>AOBScan</c> over the whole target, and Core
///         copies only the addresses inside the module or range. They do not reduce Cheat Engine's scan time or memory.
///         <see cref="AobScanRequest.MaximumResults" /> bounds only how many filtered addresses Core copies; it never stops
///         Cheat Engine early. The copied order is Cheat Engine's result-list order, which Cheat Engine does not specify.
///     </para>
///     <para>
///         Four scan limits are distinct: the Cheat Engine work limit (none on this route), the available results (the
///         host match count), the materialization limit (<see cref="AobScanRequest.MaximumResults" />), and the call
///         deadline (none: a cancellation token is observed only before dispatch and between Client-managed steps, and
///         cannot interrupt a scan that Cheat Engine has started). <see cref="IPatternScanOutcomeClient" /> reports the
///         counts and the Cheat Engine scan time separately from the Client copy time.
///     </para>
///     <para>
///         A scan that finds nothing is reported as <see cref="CheatEngineFailureKind.IndeterminateHostResult" />: this
///         scan route observes only whether Cheat Engine returned a result list, so zero matches and a host failure are
///         indistinguishable. It is never reported as <see cref="CheatEngineFailureKind.NotFound" />.
///     </para>
/// </remarks>
public interface IPatternScanner
{
	/// <summary>Tries to run one scan with managed post-filtered result materialization.</summary>
	/// <remarks>
	///     Expected failures, including an unconfirmed release of the Cheat Engine result list
	///     (<see cref="CheatEngineHostEffect.CleanupUnconfirmed" />), are returned as <paramref name="failure" />. Lifecycle
	///     faults throw <see cref="CheatEngineActivationExpiredException" /> or
	///     <see cref="CheatEngineClientLifecycleException" />.
	/// </remarks>
	public bool TryScan(AobScanRequest request, out AobScanResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs one scan with managed post-filtered result materialization or throws when it fails.</summary>
	public AobScanResult Scan(AobScanRequest request, CancellationToken cancellationToken = default);
}
