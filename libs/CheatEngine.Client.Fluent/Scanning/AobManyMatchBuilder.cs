using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable terminal builder for a bounded, copied AOB result set.</summary>
public readonly record struct AobManyMatchBuilder
{
	private readonly AobScanRequest _request;
	private readonly IPatternScanner? _scanner;

	internal AobManyMatchBuilder(IPatternScanner scanner, AobScanRequest request)
	{
		_scanner = scanner;
		_request = request;
	}

	/// <summary>Runs the bounded scan and returns its copied result set.</summary>
	/// <param name="cancellationToken">Cancels before the scan reaches Cheat Engine.</param>
	/// <returns>
	///     The bounded copied result set; inspect <see cref="AobScanResult.IsTruncated" /> before treating it as
	///     complete.
	/// </returns>
	/// <exception cref="CheatEngineOperationException">The scan operation failed or violated its materialization limit.</exception>
	public AobScanResult Execute(CancellationToken cancellationToken = default)
	{
		if (TryExecute(out AobScanResult result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		throw new CheatEngineOperationException(failure);
	}

	/// <summary>Runs the bounded scan and attempts to return its copied result set.</summary>
	/// <param name="result">The bounded copied result set when the method returns <see langword="true" />.</param>
	/// <param name="failure">The scan or materialization failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the scan reaches Cheat Engine.</param>
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
