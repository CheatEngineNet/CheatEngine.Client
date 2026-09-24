using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable terminal builder for a materialization-bounded, copied AOB result set.</summary>
/// <remarks>
///     <para>
///         The limit bounds only how many addresses Core copies from Cheat Engine's result; Cheat Engine's scan (global,
///         or bounded to the module and range) is never stopped early. Inspect <see cref="AobScanResult.IsTruncated" />
///         before treating the copy as complete.
///     </para>
///     <para>
///         An empty result means that the scan succeeded without a match inside the request: a factual zero of the
///         bounded route, or a global result list without any address inside the module or range. A global scan for
///         which Cheat Engine returns no result list is reported as
///         <see cref="CheatEngineFailureKind.IndeterminateHostResult" />, not as an empty result. Core copies at most
///         65,535 addresses, whatever the limit.
///     </para>
/// </remarks>
public readonly record struct AobManyMatchBuilder
{
	private readonly AobScanRequest _request;
	private readonly IPatternScanner? _scanner;

	internal AobManyMatchBuilder(IPatternScanner scanner, AobScanRequest request)
	{
		_scanner = scanner;
		_request = request;
	}

	/// <summary>Runs the scan and returns its materialization-bounded copied result set.</summary>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>
	///     The bounded copied result set; inspect <see cref="AobScanResult.IsTruncated" /> before treating it as
	///     complete.
	/// </returns>
	/// <exception cref="CheatEngineOperationException">
	///     The scan operation failed or violated its materialization limit.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     <paramref name="cancellationToken" /> was observed before the scan started or while Core copied its result.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The Client activation that owns the scanner has ended.</exception>
	/// <remarks>
	///     Failures are thrown through <see cref="CheatEngineFailure.Throw(CancellationToken)" />, so the exception type
	///     follows <see cref="CheatEngineFailure.Kind" />; <see cref="TryExecute" /> returns the same failure instead.
	/// </remarks>
	public AobScanResult Execute(CancellationToken cancellationToken = default)
	{
		if (TryExecute(out AobScanResult result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	/// <summary>Runs the scan and attempts to return its materialization-bounded copied result set.</summary>
	/// <param name="result">The bounded copied result set when the method returns <see langword="true" />.</param>
	/// <param name="failure">The scan or materialization failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when the bounded result set was returned.</returns>
	public bool TryExecute(out AobScanResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (!RequireScanner().TryScan(_request, out result, out failure, cancellationToken))
		{
			return false;
		}

		if (result.Matches.Length <= _request.MaximumResults)
		{
			return true;
		}

		result = default;
		failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, "Aob.Take",
			"The pattern scanner returned more matches than the request's materialization limit.");
		return false;
	}

	private IPatternScanner RequireScanner()
	{
		return _scanner ?? throw new InvalidOperationException(
			"This AOB terminal builder has no bound pattern scanner. Create it through Aob(pattern).");
	}
}
