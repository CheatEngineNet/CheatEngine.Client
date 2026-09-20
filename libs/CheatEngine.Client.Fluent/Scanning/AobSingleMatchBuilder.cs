using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable terminal builder for an AOB scan that must have exactly one match.</summary>
public readonly record struct AobSingleMatchBuilder
{
	private readonly AobScanRequest _request;
	private readonly IPatternScanner? _scanner;

	internal AobSingleMatchBuilder(IPatternScanner scanner, AobScanRequest request)
	{
		_scanner = scanner;
		_request = request;
	}

	/// <summary>Runs the scan and returns its sole match.</summary>
	/// <param name="cancellationToken">Cancels before the scan reaches Cheat Engine.</param>
	/// <returns>The sole target address.</returns>
	/// <exception cref="CheatEngineOperationException">The scan failed, had no match, or had several matches.</exception>
	public Address Execute(CancellationToken cancellationToken = default)
	{
		if (TryExecute(out Address address, out CheatEngineFailure failure, cancellationToken))
		{
			return address;
		}

		throw new CheatEngineOperationException(failure);
	}

	/// <summary>Runs the scan and attempts to return its sole match.</summary>
	/// <param name="address">The sole target address when the method returns <see langword="true" />.</param>
	/// <param name="failure">The scan or cardinality failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the scan reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when exactly one match exists.</returns>
	public bool TryExecute(out Address address, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (!RequireScanner().TryScan(_request, out AobScanResult result, out failure, cancellationToken))
		{
			address = default;
			return false;
		}

		if (result.Matches.Length > _request.MaximumResults)
		{
			address = default;
			failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, "Aob.RequireSingle",
				"The pattern scanner returned more matches than the single-match materialization limit.");
			return false;
		}

		if (result.Matches.Length == 0)
		{
			address = default;
			failure = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Aob.RequireSingle",
				"The AOB scan did not find a match.");
			return false;
		}

		if (result.Matches.Length != 1 || result.IsTruncated)
		{
			address = default;
			failure = new CheatEngineFailure(CheatEngineFailureKind.AmbiguousMatch, "Aob.RequireSingle",
				"The AOB scan found more than one match.");
			return false;
		}

		address = result.Matches[0];
		failure = default;
		return true;
	}

	private IPatternScanner RequireScanner()
	{
		return _scanner ?? throw new InvalidOperationException(
			"This AOB terminal builder has no bound pattern scanner. Create it through Aob(pattern).");
	}
}
