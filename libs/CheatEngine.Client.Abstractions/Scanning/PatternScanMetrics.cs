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
///         <c>ExaminedCount + UnreadHostRowCount == HostResultCount</c>,
///         <c>MaterializedCount + FilteredOutCount &lt;= ExaminedCount</c> and
///         <c>BelowStartSkippedCount + AtOrAfterStopSkippedCount &lt;= FilteredOutCount</c> always hold. When a
///         failure stops the copy, the counts describe the work done before the failure.
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
	/// <param name="belowStartSkippedCount">The rows the bounded route dropped below its start bound.</param>
	/// <param name="atOrAfterStopSkippedCount">The rows the bounded route dropped at or after its stop bound.</param>
	/// <param name="unreadHostRowCount">The rows that were not read, because the copy stopped first.</param>
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
		ulong filteredOutCount, int materializedCount, ulong belowStartSkippedCount, ulong atOrAfterStopSkippedCount,
		ulong unreadHostRowCount, bool inBoundsCountIsExact, TimeSpan hostScanElapsed, TimeSpan materializationElapsed)
	{
		if (!Enum.IsDefined(scope))
		{
			throw new ArgumentOutOfRangeException(nameof(scope), scope, "The pattern scan scope must be defined.");
		}

		ArgumentOutOfRangeException.ThrowIfNegative(materializedCount);
		ArgumentOutOfRangeException.ThrowIfLessThan(hostScanElapsed, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfLessThan(materializationElapsed, TimeSpan.Zero);
		if (examinedCount > hostResultCount || unreadHostRowCount != hostResultCount - examinedCount)
		{
			throw new ArgumentException(
				"The examined and unread rows must add up to the rows Cheat Engine returned.", nameof(unreadHostRowCount));
		}

		if (filteredOutCount > examinedCount || (ulong) materializedCount > examinedCount - filteredOutCount)
		{
			throw new ArgumentException(
				"Materialized and filtered-out rows cannot exceed the number of examined rows.",
				nameof(materializedCount));
		}

		if (belowStartSkippedCount > filteredOutCount ||
			atOrAfterStopSkippedCount > filteredOutCount - belowStartSkippedCount)
		{
			throw new ArgumentException("The bounded route's skipped rows are part of the filtered-out rows.",
				nameof(filteredOutCount));
		}

		if (inBoundsCountIsExact && unreadHostRowCount != 0)
		{
			throw new ArgumentException("An exact in-request count requires every row to be read.",
				nameof(inBoundsCountIsExact));
		}

		Scope = scope;
		HostResultCount = hostResultCount;
		ExaminedCount = examinedCount;
		FilteredOutCount = filteredOutCount;
		MaterializedCount = materializedCount;
		BelowStartSkippedCount = belowStartSkippedCount;
		AtOrAfterStopSkippedCount = atOrAfterStopSkippedCount;
		UnreadHostRowCount = unreadHostRowCount;
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
	public ulong BelowStartSkippedCount
	{
		get;
	}

	/// <summary>
	///     Gets the rows the bounded route dropped at or after its stop bound; expected to be zero because Cheat Engine
	///     honors it. Zero on the global routes.
	/// </summary>
	public ulong AtOrAfterStopSkippedCount
	{
		get;
	}

	/// <summary>Gets the rows that were not read because the copy stopped first.</summary>
	/// <remarks>
	///     A successful scan that left rows unread always reports a result that is not proven complete
	///     (<see cref="AobScanResult.IsTruncated" />). A truncated result can still have read every row, when the one
	///     match found beyond the copy was the last row.
	/// </remarks>
	public ulong UnreadHostRowCount
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
	/// <remarks>
	///     On <see cref="PatternScanScope.GlobalHostScanWithManagedFilter" />, when a bounded scan had run before
	///     CheatEngine.SDK lost the target's identity, it also includes that scan's time: the request cost both scans.
	/// </remarks>
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
