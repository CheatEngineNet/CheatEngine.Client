namespace CheatEngine.Client.Scanning;

/// <summary>Separates the Cheat Engine cost of one AOB scan from the Client cost of copying its result.</summary>
/// <remarks>
///     <para>
///         The four scan limits are distinct notions (audit ch.24): the <em>Cheat Engine work limit</em> (the bounds on
///         <see cref="PatternScanScope.HostBoundedRange" />, none on the global routes), the <em>available results</em>
///         (<see cref="HostResultCount" />), the <em>materialization limit</em>
///         (<see cref="AobScanRequest.MaximumResults" />, which bounds <see cref="MaterializedCount" />), and the
///         <em>call deadline</em> (none: cancellation is observed only between Cheat Engine calls and Client-managed steps
///         and never interrupts a started Cheat Engine scan).
///     </para>
///     <para>
///         Counts and durations are safe to log; they never contain addresses or values. The invariants
///         <c>ExaminedCount + UnreadHostRows == HostResultCount</c>,
///         <c>MaterializedCount + FilteredOutCount &lt;= ExaminedCount</c> and
///         <c>BelowStartSkipped + AtOrAfterStopSkipped &lt;= FilteredOutCount</c> always hold. When a failure stops the
///         copy, the counts describe the work done before the failure.
///     </para>
/// </remarks>
public readonly record struct PatternScanMetrics
{
	/// <summary>Creates validated scan metrics.</summary>
	/// <param name="scope">The part of the target that Cheat Engine scanned.</param>
	/// <param name="hostResultCount">The number of rows Cheat Engine returned, including rows outside the request.</param>
	/// <param name="examinedCount">The number of rows Core or CheatEngine.SDK read.</param>
	/// <param name="filteredOutCount">The number of examined rows outside the module or range.</param>
	/// <param name="materializedCount">The number of examined rows Core copied into the result.</param>
	/// <param name="belowStartSkipped">The rows the bounded route dropped below its start bound.</param>
	/// <param name="atOrAfterStopSkipped">The rows the bounded route dropped at or after its stop bound.</param>
	/// <param name="unreadHostRows">The rows that were not read, because the copy stopped first.</param>
	/// <param name="inBoundsCountIsExact">
	///     Whether every row was read, so the number of in-request matches is exactly
	///     <c>ExaminedCount - FilteredOutCount</c>.
	/// </param>
	/// <param name="hostScanElapsed">The elapsed time of the Cheat Engine scan call only.</param>
	/// <param name="materializationElapsed">The elapsed time of the count read, copy, parse, and filter steps.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     A count or duration is negative, or the scope is undefined.
	/// </exception>
	/// <exception cref="ArgumentException">The counts violate the documented invariants.</exception>
	public PatternScanMetrics(PatternScanScope scope, ulong hostResultCount, ulong examinedCount,
		ulong filteredOutCount, int materializedCount, ulong belowStartSkipped, ulong atOrAfterStopSkipped,
		ulong unreadHostRows, bool inBoundsCountIsExact, TimeSpan hostScanElapsed, TimeSpan materializationElapsed)
	{
		if (!Enum.IsDefined(scope))
		{
			throw new ArgumentOutOfRangeException(nameof(scope), scope, "The pattern scan scope must be defined.");
		}

		ArgumentOutOfRangeException.ThrowIfNegative(materializedCount);
		ArgumentOutOfRangeException.ThrowIfLessThan(hostScanElapsed, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfLessThan(materializationElapsed, TimeSpan.Zero);
		if (examinedCount > hostResultCount || unreadHostRows != hostResultCount - examinedCount)
		{
			throw new ArgumentException(
				"The examined and unread rows must add up to the rows Cheat Engine returned.", nameof(unreadHostRows));
		}

		if (filteredOutCount > examinedCount || (ulong) materializedCount > examinedCount - filteredOutCount)
		{
			throw new ArgumentException(
				"Materialized and filtered-out rows cannot exceed the number of examined rows.",
				nameof(materializedCount));
		}

		if (belowStartSkipped > filteredOutCount || atOrAfterStopSkipped > filteredOutCount - belowStartSkipped)
		{
			throw new ArgumentException("The bounded route's skipped rows are part of the filtered-out rows.",
				nameof(filteredOutCount));
		}

		if (inBoundsCountIsExact && unreadHostRows != 0)
		{
			throw new ArgumentException("An exact in-request count requires every row to be read.",
				nameof(inBoundsCountIsExact));
		}

		Scope = scope;
		HostResultCount = hostResultCount;
		ExaminedCount = examinedCount;
		FilteredOutCount = filteredOutCount;
		MaterializedCount = materializedCount;
		BelowStartSkipped = belowStartSkipped;
		AtOrAfterStopSkipped = atOrAfterStopSkipped;
		UnreadHostRows = unreadHostRows;
		InBoundsCountIsExact = inBoundsCountIsExact;
		HostScanElapsed = hostScanElapsed;
		MaterializationElapsed = materializationElapsed;
	}

	/// <summary>Gets the part of the target that Cheat Engine scanned.</summary>
	public PatternScanScope Scope
	{
		get;
	}

	/// <summary>
	///     Gets the number of rows Cheat Engine returned (the available results); on the bounded route it includes rows
	///     outside the bounds.
	/// </summary>
	public ulong HostResultCount
	{
		get;
	}

	/// <summary>Gets the number of rows Core or CheatEngine.SDK read before the copy stopped.</summary>
	/// <remarks>Lower than <see cref="HostResultCount" /> when the materialization limit or a failure stopped the copy.</remarks>
	public ulong ExaminedCount
	{
		get;
	}

	/// <summary>
	///     Gets the number of examined rows outside the request: removed by the managed module or range filters, or by
	///     the bounded route's own start and stop checks.
	/// </summary>
	public ulong FilteredOutCount
	{
		get;
	}

	/// <summary>Gets the number of examined rows Core copied into the result.</summary>
	public int MaterializedCount
	{
		get;
	}

	/// <summary>
	///     Gets the rows the bounded route dropped because they began below its start: Cheat Engine's start bound is not
	///     byte-exact. Zero on the global routes.
	/// </summary>
	public ulong BelowStartSkipped
	{
		get;
	}

	/// <summary>
	///     Gets the rows the bounded route dropped at or after its stop bound; expected to be zero because Cheat Engine
	///     honors it. Zero on the global routes.
	/// </summary>
	public ulong AtOrAfterStopSkipped
	{
		get;
	}

	/// <summary>Gets the rows that were not read because the copy stopped first.</summary>
	public ulong UnreadHostRows
	{
		get;
	}

	/// <summary>
	///     Gets whether every row was read, so the number of in-request matches is exactly
	///     <c>ExaminedCount - FilteredOutCount</c>. Uniqueness needs an exact count or a second match, never a first-found
	///     scan.
	/// </summary>
	public bool InBoundsCountIsExact
	{
		get;
	}

	/// <summary>Gets the elapsed time of the Cheat Engine scan call only (host scan time).</summary>
	public TimeSpan HostScanElapsed
	{
		get;
	}

	/// <summary>Gets the elapsed time spent reading the count, copying, parsing, and filtering the result.</summary>
	public TimeSpan MaterializationElapsed
	{
		get;
	}
}
