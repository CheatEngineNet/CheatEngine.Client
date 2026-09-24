namespace CheatEngine.Client.Scanning;

/// <summary>Separates the Cheat Engine cost of one AOB scan from the Client cost of copying its result.</summary>
/// <remarks>
///     <para>
///         The four scan limits are distinct notions (audit ch.24): the <em>Cheat Engine work limit</em> (the bounds on
///         <see cref="PatternScanScope.HostBoundedRange" />, none on the global routes), the <em>available results</em>
///         (<see cref="HostMatchCount" />), the <em>materialization limit</em>
///         (<see cref="AobScanRequest.MaximumResults" />, which bounds <see cref="MaterializedCount" />), and the
///         <em>call deadline</em> (none: cancellation is observed only between Cheat Engine calls and Client-managed steps
///         and never interrupts a started Cheat Engine scan).
///     </para>
///     <para>
///         Counts and durations are safe to log; they never contain addresses or values. The invariants
///         <c>MaterializedCount + FilteredOutCount &lt;= ExaminedCount &lt;= HostMatchCount</c> always hold. When a failure
///         stops the copy, the counts describe the work done before the failure.
///     </para>
/// </remarks>
public readonly record struct PatternScanMetrics
{
	/// <summary>Creates validated scan metrics.</summary>
	/// <param name="hostMatchCount">The number of entries in Cheat Engine's result list.</param>
	/// <param name="examinedCount">The number of entries Core read and parsed.</param>
	/// <param name="filteredOutCount">The number of examined entries removed by the module or range post-filters.</param>
	/// <param name="materializedCount">The number of examined entries Core copied into the result.</param>
	/// <param name="scope">The part of the target that Cheat Engine scanned.</param>
	/// <param name="hostScanElapsed">The elapsed time of the Cheat Engine scan call only.</param>
	/// <param name="materializationElapsed">The elapsed time of the count read, copy, parse, and filter steps.</param>
	/// <exception cref="ArgumentOutOfRangeException">A count or duration is negative, or the scope is undefined.</exception>
	/// <exception cref="ArgumentException">The counts violate the documented invariants.</exception>
	public PatternScanMetrics(int hostMatchCount, int examinedCount, int filteredOutCount, int materializedCount,
		PatternScanScope scope, TimeSpan hostScanElapsed, TimeSpan materializationElapsed)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(hostMatchCount);
		ArgumentOutOfRangeException.ThrowIfNegative(examinedCount);
		ArgumentOutOfRangeException.ThrowIfNegative(filteredOutCount);
		ArgumentOutOfRangeException.ThrowIfNegative(materializedCount);
		ArgumentOutOfRangeException.ThrowIfLessThan(hostScanElapsed, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfLessThan(materializationElapsed, TimeSpan.Zero);
		if (!Enum.IsDefined(scope))
		{
			throw new ArgumentOutOfRangeException(nameof(scope), scope, "The pattern scan scope must be defined.");
		}

		if (examinedCount > hostMatchCount)
		{
			throw new ArgumentException("Core cannot examine more entries than Cheat Engine returned.",
				nameof(examinedCount));
		}

		if ((long) materializedCount + filteredOutCount > examinedCount)
		{
			throw new ArgumentException(
				"Materialized and filtered-out entries cannot exceed the number of examined entries.",
				nameof(materializedCount));
		}

		HostMatchCount = hostMatchCount;
		ExaminedCount = examinedCount;
		FilteredOutCount = filteredOutCount;
		MaterializedCount = materializedCount;
		Scope = scope;
		HostScanElapsed = hostScanElapsed;
		MaterializationElapsed = materializationElapsed;
	}

	/// <summary>
	///     Gets the number of entries Cheat Engine returned (the available results), saturated at
	///     <see cref="int.MaxValue" />; on the bounded route it includes rows outside the bounds.
	/// </summary>
	public int HostMatchCount
	{
		get;
	}

	/// <summary>Gets the number of entries Core read and parsed before it stopped copying.</summary>
	/// <remarks>Lower than <see cref="HostMatchCount" /> when the materialization limit or a failure stopped the copy.</remarks>
	public int ExaminedCount
	{
		get;
	}

	/// <summary>
	///     Gets the number of examined entries outside the request: removed by the managed module or range filters, or by
	///     the bounded route's own start and stop checks.
	/// </summary>
	public int FilteredOutCount
	{
		get;
	}

	/// <summary>Gets the number of examined entries Core copied into the result.</summary>
	public int MaterializedCount
	{
		get;
	}

	/// <summary>Gets the part of the target that Cheat Engine scanned.</summary>
	public PatternScanScope Scope
	{
		get;
	}

	/// <summary>Gets the elapsed time of the Cheat Engine scan call only (host scan time).</summary>
	public TimeSpan HostScanElapsed
	{
		get;
	}

	/// <summary>Gets the elapsed time Core spent reading the count, copying, parsing, and filtering the result list.</summary>
	public TimeSpan MaterializationElapsed
	{
		get;
	}
}
