using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>
///     The detailed outcome of one AOB scan: its copied result or failure, the scan metrics, the host's own outcome, why
///     the scan ran on its route, and whether its target identity was verified.
/// </summary>
/// <remarks>
///     <para>
///         Exactly one of <see cref="Result" /> and <see cref="Failure" /> is present. <see cref="Metrics" /> is present on
///         every success, and on a failure whenever Cheat Engine returned a result whose count the Client could read (for
///         example a cancellation observed while copying). It is <see langword="null" /> when no result was obtained, as
///         for the <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> outcome of a global scan that returned
///         <c>nil</c>.
///     </para>
///     <para>
///         <see cref="Result" /> and <see cref="Failure" /> are classified exactly as
///         <see cref="IPatternScanner.TryScan" /> classifies them for the same request and host behavior.
///     </para>
///     <para>
///         A successful result whose <see cref="AobScanResult.IsTruncated" /> is <see langword="true" /> is not
///         proven complete. <see cref="Metrics" /> says why: <see cref="PatternScanMetrics.UnreadHostRowCount" />
///         counts the rows left unread, and <see cref="PatternScanMetrics.InBoundsCountIsExact" /> says whether every
///         row was read.
///     </para>
/// </remarks>
public sealed class PatternScanOutcome
{
	/// <summary>Creates a validated scan outcome.</summary>
	/// <param name="result">The copied result of a successful scan, or <see langword="null" /> on failure.</param>
	/// <param name="failure">The failure, or <see langword="null" /> on success.</param>
	/// <param name="metrics">The scan metrics; required on success.</param>
	/// <param name="hostOutcome">What Cheat Engine reported for the scan that ran.</param>
	/// <param name="routeReason">Why the scan ran on its route.</param>
	/// <param name="targetIdentityVerified">
	///     Whether the copied addresses were attributed to one qualified target incarnation for the whole scan.
	/// </param>
	/// <exception cref="ArgumentException">
	///     Both or neither of <paramref name="result" /> and <paramref name="failure" /> are present, the failure is not a
	///     classified failure, a success has no metrics, the metrics contradict the copied result, a success reports a host
	///     outcome other than <see cref="PatternScanHostOutcomeKind.Matches" /> or
	///     <see cref="PatternScanHostOutcomeKind.NoMatches" /> or no route, or a failure reports a verified target.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="hostOutcome" /> or <paramref name="routeReason" /> is not a defined value.
	/// </exception>
	public PatternScanOutcome(AobScanResult? result, CheatEngineFailure? failure, PatternScanMetrics? metrics,
		PatternScanHostOutcomeKind hostOutcome, PatternScanRouteReason routeReason, bool targetIdentityVerified)
	{
		if (!Enum.IsDefined(hostOutcome))
		{
			throw new ArgumentOutOfRangeException(nameof(hostOutcome), hostOutcome,
				"The pattern scan host outcome must be defined.");
		}

		if (!Enum.IsDefined(routeReason))
		{
			throw new ArgumentOutOfRangeException(nameof(routeReason), routeReason,
				"The pattern scan route reason must be defined.");
		}

		if (result.HasValue == failure.HasValue)
		{
			throw new ArgumentException("A pattern scan outcome requires exactly one of a result or a failure.",
				nameof(failure));
		}

		if (failure is { } cause)
		{
			if (string.IsNullOrWhiteSpace(cause.Operation))
			{
				throw new ArgumentException("A failed pattern scan outcome requires a classified failure.",
					nameof(failure));
			}

			if (targetIdentityVerified)
			{
				throw new ArgumentException("A failed pattern scan publishes no address to attribute to a target.",
					nameof(targetIdentityVerified));
			}
		}

		if (result is { } copied)
		{
			ValidateSuccess(copied, metrics, hostOutcome, routeReason);
		}

		Result = result;
		Failure = failure;
		Metrics = metrics;
		HostOutcome = hostOutcome;
		RouteReason = routeReason;
		TargetIdentityVerified = targetIdentityVerified;
	}

	/// <summary>Gets whether the scan succeeded.</summary>
	public bool IsSuccess => Failure is null;

	/// <summary>Gets the copied result of a successful scan.</summary>
	public AobScanResult? Result
	{
		get;
	}

	/// <summary>Gets the failure of an unsuccessful scan.</summary>
	public CheatEngineFailure? Failure
	{
		get;
	}

	/// <summary>Gets the host and copy metrics, when Cheat Engine returned a result with a readable count.</summary>
	public PatternScanMetrics? Metrics
	{
		get;
	}

	/// <summary>Gets what Cheat Engine reported for the scan that ran, before the Client decided the result.</summary>
	public PatternScanHostOutcomeKind HostOutcome
	{
		get;
	}

	/// <summary>Gets why the scan ran on its route; <see cref="PatternScanRouteReason.Unknown" /> when none ran.</summary>
	public PatternScanRouteReason RouteReason
	{
		get;
	}

	/// <summary>
	///     Gets whether the copied addresses are attributed to one qualified local target incarnation for the whole scan:
	///     always on a successful bounded scan, and on an unscoped global scan only when Cheat Engine's selection was the
	///     same qualified incarnation before and after the call. <see langword="false" /> for an unqualified target
	///     (CEServer, file as process), on every failure, and always on the
	///     <see cref="PatternScanScope.GlobalHostScanWithManagedFilter" /> route, whose
	///     <see cref="PatternScanRouteReason.TargetIdentityNotQualified" /> reason it never contradicts.
	/// </summary>
	public bool TargetIdentityVerified
	{
		get;
	}

	private static void ValidateSuccess(AobScanResult copied, PatternScanMetrics? metrics,
		PatternScanHostOutcomeKind hostOutcome, PatternScanRouteReason routeReason)
	{
		if (metrics is not { } measured)
		{
			throw new ArgumentException("A successful pattern scan outcome requires its metrics.", nameof(metrics));
		}

		if (measured.MaterializedCount != copied.Matches.Length)
		{
			throw new ArgumentException("The materialized count must equal the number of copied matches.",
				nameof(metrics));
		}

		if (hostOutcome is not (PatternScanHostOutcomeKind.Matches or PatternScanHostOutcomeKind.NoMatches))
		{
			throw new ArgumentException("A successful pattern scan reports the Matches or NoMatches host outcome.",
				nameof(hostOutcome));
		}

		if (routeReason == PatternScanRouteReason.Unknown)
		{
			throw new ArgumentException("A successful pattern scan reports the route it ran on.", nameof(routeReason));
		}
	}
}
