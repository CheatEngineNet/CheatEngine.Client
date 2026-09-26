using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>Runs the scan of one AOB terminal and checks what the scanner published before a terminal judges it.</summary>
/// <remarks>
///     <para>
///         Every terminal reads <see cref="IPatternScanner.ScanDetailed" />, whose result and failure are classified exactly
///         as <see cref="IPatternScanner.TryScan" /> classifies them, because its metrics say whether every row Cheat
///         Engine returned was read (<see cref="PatternScanMetrics.InBoundsCountIsExact" />). Only such an exhaustive read
///         makes "no match inside the request" a fact: <c>FirstOrNone</c> returns <see langword="null" />,
///         <c>RequireSingle</c> reports <see cref="CheatEngineFailureKind.NotFound" /> and <c>Take</c> returns an empty
///         result only then.
///     </para>
///     <para>
///         The routes' factual zeros are the bounded route's empty in-bounds result
///         (<see cref="PatternScanHostOutcomeKind.NoMatches" />, or rows that all lie outside the request), a global
///         result list whose rows all lie outside the module or range
///         (<see cref="PatternScanScope.GlobalHostScanWithManagedFilter" />), and an empty list that a global scan does
///         return. A global scan that returns no list (<see cref="PatternScanHostOutcomeKind.NoResult" />, the shape of
///         zero matches on Cheat Engine 7.7) is already a <see cref="CheatEngineFailureKind.IndeterminateHostResult" />
///         failure of the scanner. No terminal uses a "first found" scan.
///     </para>
/// </remarks>
internal static class AobTerminal
{
	/// <summary>Runs the scan and returns its copied result, or the failure the terminal must report.</summary>
	/// <param name="scanner">The bound scanner.</param>
	/// <param name="request">The request whose <see cref="AobScanRequest.MaximumResults" /> is the terminal's copy limit.</param>
	/// <param name="operation">The terminal's operation name, used by the failures this method creates.</param>
	/// <param name="result">The copied result when the method returns <see langword="true" />.</param>
	/// <param name="isExhaustive">Whether every row Cheat Engine returned was read.</param>
	/// <param name="failure">The scanner's failure, or the one this method creates, on <see langword="false" />.</param>
	/// <param name="cancellationToken">The caller's token, passed unchanged to the scanner.</param>
	/// <returns>
	///     <see langword="true" /> when the scan succeeded within the copy limit and an empty result was read exhaustively.
	/// </returns>
	internal static bool TryScan(IPatternScanner scanner, AobScanRequest request, string operation,
		out AobScanResult result, out bool isExhaustive, out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		PatternScanOutcome outcome = scanner.ScanDetailed(request, cancellationToken);
		result = default;
		isExhaustive = false;
		if (outcome.Failure is { } cause)
		{
			failure = cause;
			return false;
		}

		if (outcome.Result is not { } copied || outcome.Metrics is not { } metrics)
		{
			failure = Fail(CheatEngineFailureKind.InvalidHostResult, operation,
				"The pattern scanner reported a success without a copied result and its metrics.");
			return false;
		}

		if (copied.Matches.Length > request.MaximumResults)
		{
			failure = Fail(CheatEngineFailureKind.InvalidHostResult, operation,
				"The pattern scanner returned more matches than the terminal's materialization limit.");
			return false;
		}

		if (copied.Matches.IsEmpty && !metrics.InBoundsCountIsExact)
		{
			failure = Unread(operation);
			return false;
		}

		result = copied;
		isExhaustive = metrics.InBoundsCountIsExact;
		failure = default;
		return true;
	}

	/// <summary>
	///     Creates the failure of a terminal whose answer needs a row the scan did not read: whether a (further) match
	///     lies inside the request is unknown.
	/// </summary>
	/// <param name="operation">The terminal's operation name.</param>
	/// <returns>An <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> failure after a completed scan.</returns>
	internal static CheatEngineFailure Unread(string operation)
	{
		return Fail(CheatEngineFailureKind.IndeterminateHostResult, operation,
			"The AOB scan did not read every row Cheat Engine returned, so whether a match inside the request is " +
			"missing from the copy is unknown.");
	}

	/// <summary>Creates a terminal failure after a completed scan whose Cheat Engine resources were released.</summary>
	/// <param name="kind">The failure kind.</param>
	/// <param name="operation">The terminal's operation name.</param>
	/// <param name="message">The failure message; it never contains an address.</param>
	/// <returns>The failure, with <see cref="CheatEngineHostEffect.Completed" />.</returns>
	internal static CheatEngineFailure Fail(CheatEngineFailureKind kind, string operation, string message)
	{
		return new CheatEngineFailure(kind, operation, message, null, CheatEngineHostEffect.Completed);
	}
}
