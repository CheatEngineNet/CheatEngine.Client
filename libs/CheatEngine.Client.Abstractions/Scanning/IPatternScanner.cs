using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>Runs AOB scans on the cheapest route the request allows and copies their addresses before releasing SDK-owned objects.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <b>Routes: what they answer and what they cost.</b> Core picks one of three routes for each request;
///         <see cref="PatternScanMetrics.Scope" /> names the one that ran and <see cref="PatternScanOutcome.RouteReason" />
///         says why:
///     </para>
///     <list type="table">
///         <listheader>
///             <term>Route</term>
///             <description>Answer and cost</description>
///         </listheader>
///         <item>
///             <term><see cref="PatternScanScope.GlobalHostScan" /></term>
///             <description>
///                 A request without <see cref="AobScanRequest.Module" /> or <see cref="AobScanRequest.Range" />
///                 (<see cref="PatternScanRouteReason.UnscopedRequest" />). Cheat Engine runs one global <c>AOBScan</c>
///                 over the whole target: the cost is a full scan whatever the materialization limit. Matches are exact,
///                 but zero matches are indeterminate (<see cref="PatternScanHostOutcome.NoResult" />).
///             </description>
///         </item>
///         <item>
///             <term><see cref="PatternScanScope.HostBoundedRange" /></term>
///             <description>
///                 A module and/or range request on a target that Cheat Engine reports as a qualified local process
///                 (<see cref="PatternScanRouteReason.ScopedRequestOnQualifiedTarget" />). Cheat Engine runs an exhaustive
///                 MemScan limited to the module intersected with the range, so its cost is proportional to those bounds,
///                 and zero matches are a factual empty result when Cheat Engine's error text was readable. The call
///                 blocks Cheat Engine's main thread for the scan, the copy and the release, and a started scan cannot be
///                 interrupted in 1.0: cancellation is observed only between Cheat Engine calls. A match must lie entirely
///                 inside the module, and a match starting at the range end is included.
///             </description>
///         </item>
///         <item>
///             <term><see cref="PatternScanScope.GlobalHostScanWithManagedFilter" /></term>
///             <description>
///                 A module and/or range request whose bounded route cannot run
///                 (<see cref="PatternScanRouteReason.TargetIdentityNotQualified" />): an unqualified target such as a
///                 CEServer or file-as-process selection, or a scan session CheatEngine.SDK could not create or attach to
///                 one target. Cheat Engine runs the global scan, at the cost of a full scan, and Core keeps only the
///                 addresses whose start lies inside the module and range; zero matches are indeterminate as on the global
///                 route.
///             </description>
///         </item>
///     </list>
///     <para>
///         Core resolves the module before any scan, and a range that does not overlap the module is refused before any
///         scan. <see cref="AobScanRequest.MaximumResults" /> bounds only how many addresses Core copies; it never stops
///         Cheat Engine early, and the bounded route copies at most 65,535 addresses. The copied order is Cheat Engine's
///         result-list order, which Cheat Engine does not specify.
///     </para>
///     <para>
///         Four scan limits are distinct: the Cheat Engine work limit (the bounds on the bounded route, none on the global
///         routes), the available results (<see cref="PatternScanMetrics.HostResultCount" />), the materialization limit
///         (<see cref="AobScanRequest.MaximumResults" />), and the call deadline (none: a cancellation token is observed
///         only between Cheat Engine calls and Client-managed steps, and cannot interrupt a scan that Cheat Engine has
///         started). <see cref="ScanDetailed" /> reports the counts and the Cheat Engine scan time separately from the
///         Client copy time.
///     </para>
///     <para>
///         On a global route a scan that finds nothing is reported as
///         <see cref="CheatEngineFailureKind.IndeterminateHostResult" />: on Cheat Engine 7.7 <c>AOBScan</c> returns
///         <c>nil</c> for zero matches, and a host failure can return the same shape, so that route cannot tell them
///         apart. It is never reported as <see cref="CheatEngineFailureKind.NotFound" />. The other host outcomes keep
///         their own kinds: an absent <c>AOBScan</c> is <see cref="CheatEngineFailureKind.CapabilityUnavailable" />, a
///         raising call is <see cref="CheatEngineFailureKind.LuaError" />, a malformed result is
///         <see cref="CheatEngineFailureKind.InvalidHostResult" />, and an empty list that Cheat Engine does return is a
///         successful no-match. When Cheat Engine's selected target changes during the scan, its addresses are discarded
///         and the scan fails with <see cref="CheatEngineFailureKind.TargetChanged" /> or
///         <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" />.
///     </para>
/// </remarks>
public interface IPatternScanner
{
	/// <summary>Tries to run one scan and copy its matches.</summary>
	/// <remarks>
	///     Expected failures, including an unconfirmed release of the Cheat Engine result list or scan session
	///     (<see cref="CheatEngineHostEffect.CleanupUnconfirmed" />), are returned as <paramref name="failure" />. Lifecycle
	///     faults throw <see cref="CheatEngineActivationExpiredException" /> or
	///     <see cref="CheatEngineClientLifecycleException" />.
	/// </remarks>
	public bool TryScan(AobScanRequest request, out AobScanResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs one scan and copies its matches, or throws when it fails.</summary>
	public AobScanResult Scan(AobScanRequest request, CancellationToken cancellationToken = default);

	/// <summary>Runs one scan and returns its detailed outcome.</summary>
	/// <param name="request">The scan request.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch, between Cheat Engine calls and between Client-managed steps; it never interrupts a
	///     Cheat Engine scan that has already started (see <see cref="CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>
	///     The detailed outcome: the result or failure <see cref="TryScan" /> would return, the metrics, the host's own
	///     outcome, the route reason and whether the target identity was verified. Expected failures are returned in
	///     <see cref="PatternScanOutcome.Failure" />.
	/// </returns>
	/// <exception cref="CheatEngineActivationExpiredException">The client activation has expired.</exception>
	/// <exception cref="CheatEngineClientLifecycleException">The client activation is stopping.</exception>
	public PatternScanOutcome ScanDetailed(AobScanRequest request, CancellationToken cancellationToken = default);
}
