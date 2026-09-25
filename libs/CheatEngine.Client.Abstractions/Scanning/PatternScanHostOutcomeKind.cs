namespace CheatEngine.Client.Scanning;

/// <summary>What Cheat Engine, through CheatEngine.SDK, reported for the scan that ran.</summary>
/// <remarks>
///     <para>
///         The value is the host's own outcome, before the Client decides the result: a global scan that reported
///         <see cref="Matches" /> can still fail, for example when the selected target changed during the call or when the
///         result list release was not confirmed; <see cref="PatternScanOutcome.Failure" /> carries that verdict. For a
///         request that fell back from the bounded route, the value is the global scan's outcome.
///     </para>
///     <para>
///         The global <c>AOBScan</c> route reports <see cref="Matches" />, <see cref="NoMatches" />,
///         <see cref="NoResult" />, <see cref="GlobalUnavailable" />, <see cref="ProtectedLuaFailure" />,
///         <see cref="InvalidResult" /> or <see cref="ResultListCountUnavailable" />. The bounded route reports
///         <see cref="Matches" />, <see cref="NoMatches" />, <see cref="HostReportedError" />, <see cref="InvalidResult" />,
///         <see cref="TargetChanged" />, <see cref="TargetIdentityUnavailable" />, <see cref="RuntimeChanged" />,
///         <see cref="Cancelled" /> or <see cref="ProtectedLuaFailure" />: a fact that both routes report has one member.
///         <see cref="Unknown" /> means no host outcome was observed (the request was refused or failed before a scan)
///         or the outcome is not one this Client knows.
///     </para>
/// </remarks>
public enum PatternScanHostOutcomeKind
{
	/// <summary>No host outcome was observed, or it is not one this Client version knows.</summary>
	Unknown = 0,

	/// <summary>Cheat Engine returned at least one match.</summary>
	Matches = 1,

	/// <summary>
	///     Cheat Engine returned no match: a valid empty list on the global route (not observed on Cheat Engine 7.7), or
	///     no in-bounds row on the bounded route.
	/// </summary>
	NoMatches = 2,

	/// <summary>
	///     The global <c>AOBScan</c> returned <c>nil</c>: on Cheat Engine 7.7 zero matches and host failures share this
	///     shape.
	/// </summary>
	NoResult = 3,

	/// <summary>The <c>AOBScan</c> global was absent or not callable.</summary>
	GlobalUnavailable = 4,

	/// <summary>
	///     A protected Lua call of the scan failed: the global <c>AOBScan</c> lookup or call, or a call of the bounded
	///     scan (its scan, wait, count or row reads).
	/// </summary>
	ProtectedLuaFailure = 5,

	/// <summary>Cheat Engine returned a malformed value, count or row.</summary>
	InvalidResult = 6,

	/// <summary>The global route's result list had no readable count.</summary>
	ResultListCountUnavailable = 7,

	/// <summary>The bounded scan completed without an in-bounds row while Cheat Engine reported an error text.</summary>
	HostReportedError = 8,

	/// <summary>The bounded scan's target was no longer the incarnation it started on.</summary>
	TargetChanged = 9,

	/// <summary>The bounded scan's target could not be qualified during the scan.</summary>
	TargetIdentityUnavailable = 10,

	/// <summary>The Lua runtime changed during the bounded scan.</summary>
	RuntimeChanged = 11,

	/// <summary>CheatEngine.SDK observed the cancellation token between the bounded scan's Cheat Engine calls.</summary>
	Cancelled = 12
}
