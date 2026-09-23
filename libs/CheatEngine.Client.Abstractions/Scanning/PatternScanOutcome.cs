using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>The detailed outcome of one AOB scan: its copied result or failure, plus the scan metrics.</summary>
/// <remarks>
///     <para>
///         Exactly one of <see cref="Result" /> and <see cref="Cause" /> is present. <see cref="Metrics" /> is present on
///         every success, and on a failure whenever Cheat Engine returned a result list whose count Core could read (for
///         example a cancellation observed while copying). It is <see langword="null" /> when no list was obtained, as
///         for the CheatEngine.SDK 1.0.0 <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> outcome.
///     </para>
///     <para>
///         <see cref="Result" /> and <see cref="Cause" /> are classified exactly as
///         <see cref="IPatternScanner.TryScan" /> classifies them for the same request and host behavior.
///     </para>
/// </remarks>
public sealed class PatternScanOutcome
{
	/// <summary>Creates a validated scan outcome.</summary>
	/// <param name="result">The copied result of a successful scan, or <see langword="null" /> on failure.</param>
	/// <param name="cause">The failure, or <see langword="null" /> on success.</param>
	/// <param name="metrics">The scan metrics; required on success.</param>
	/// <exception cref="ArgumentException">
	///     Both or neither of <paramref name="result" /> and <paramref name="cause" /> are present, the cause is not a
	///     classified failure, a success has no metrics, or the metrics contradict the copied result.
	/// </exception>
	public PatternScanOutcome(AobScanResult? result, CheatEngineFailure? cause, PatternScanMetrics? metrics)
	{
		if (result.HasValue == cause.HasValue)
		{
			throw new ArgumentException("A pattern scan outcome requires exactly one of a result or a cause.",
				nameof(cause));
		}

		if (cause is { } failure && string.IsNullOrWhiteSpace(failure.Operation))
		{
			throw new ArgumentException("A failed pattern scan outcome requires a classified failure.", nameof(cause));
		}

		if (result is { } copied)
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
		}

		Result = result;
		Cause = cause;
		Metrics = metrics;
	}

	/// <summary>Gets the copied result of a successful scan.</summary>
	public AobScanResult? Result
	{
		get;
	}

	/// <summary>Gets the failure of an unsuccessful scan.</summary>
	public CheatEngineFailure? Cause
	{
		get;
	}

	/// <summary>Gets the host and copy metrics, when Cheat Engine returned a result list with a readable count.</summary>
	public PatternScanMetrics? Metrics
	{
		get;
	}

	/// <summary>Gets whether the scan succeeded.</summary>
	public bool Succeeded => Cause is null;
}
