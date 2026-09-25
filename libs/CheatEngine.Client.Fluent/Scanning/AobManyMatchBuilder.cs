using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable terminal builder for a materialization-bounded, copied AOB result set.</summary>
/// <remarks>
///     <para>
///         Create it with <see cref="AobScanBuilder.Take" />. It is a plain value that declares no <c>Equals</c>,
///         <c>GetHashCode</c>, <c>ToString</c> or equality operators (only those inherited from <see cref="ValueType" />):
///         compare the requests it runs, not the builders. Its only constructor is the implicit parameterless one, which
///         yields the <see langword="default" /> value: that value has no pattern scanner, and its terminals throw
///         <see cref="InvalidOperationException" />.
///     </para>
///     <para>
///         The limit bounds only how many addresses Core copies from Cheat Engine's result, and every route copies at
///         most 65,535 addresses whatever the limit; Cheat Engine's scan (global, or bounded to the module and range) is
///         never stopped early. Inspect <see cref="AobScanResult.IsTruncated" /> before treating the copy as complete.
///     </para>
///     <para>
///         An empty result is a factual zero: the scan succeeded, every row Cheat Engine returned was read, and none lay
///         inside the request. That is the bounded route's empty in-bounds result, a global result list whose rows all lie
///         outside the module or range, or an empty list that a global scan does return. A global scan for which Cheat
///         Engine returns no result list is reported as <see cref="CheatEngineFailureKind.IndeterminateHostResult" />, and
///         so is an empty copy that did not read every row: neither is ever an empty result.
///     </para>
/// </remarks>
public readonly struct AobManyMatchBuilder
{
	private const string Operation = "Patterns.Scan";
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
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no pattern scanner.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The scan operation failed or violated its materialization limit.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     <paramref name="cancellationToken" /> was observed before the scan started or while Core copied its result.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The Client activation that owns the scanner has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping and admits no new work, or the scan failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
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
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no pattern scanner.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The Client activation that owns the scanner has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping and admits no new work.
	/// </exception>
	public bool TryExecute(out AobScanResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return AobTerminal.TryScan(RequireScanner(), _request, Operation, out result, out _, out failure,
			cancellationToken);
	}

	private IPatternScanner RequireScanner()
	{
		return _scanner ?? throw new InvalidOperationException(
			"This AOB terminal builder is a default value without a pattern scanner. Select it from " +
			"scanner.Aob(pattern), for example client.Patterns.Aob(pattern).Take(maximumResults).");
	}
}
