namespace CheatEngine.Client.Scanning;

/// <summary>Why the scan ran on its route (<see cref="PatternScanMetrics.Scope" />).</summary>
public enum PatternScanRouteReason
{
	/// <summary>No route was chosen: the request was refused or failed before a scan started.</summary>
	Unknown = 0,

	/// <summary>
	///     The request has no module and no range, so Cheat Engine ran one global <c>AOBScan</c>
	///     (<see cref="PatternScanScope.GlobalHostScan" />).
	/// </summary>
	UnscopedRequest = 1,

	/// <summary>
	///     The request has a module and/or a range and Cheat Engine's selected target is a qualified local process, so
	///     Cheat Engine ran the bounded, exhaustive scan (<see cref="PatternScanScope.HostBoundedRange" />).
	/// </summary>
	ScopedRequestOnQualifiedTarget = 2,

	/// <summary>
	///     The request has a module and/or a range, but the bounded route could not run: the target was not qualified
	///     (a CEServer, file-as-process or unobservable selection), or CheatEngine.SDK could not create the scan session or
	///     qualify the target during the scan. Cheat Engine ran the global scan with managed filters
	///     (<see cref="PatternScanScope.GlobalHostScanWithManagedFilter" />), and the outcome's
	///     <see cref="PatternScanOutcome.TargetIdentityVerified" /> is always <see langword="false" />, even when the global
	///     scan itself saw one qualified incarnation.
	/// </summary>
	TargetIdentityNotQualified = 3
}
