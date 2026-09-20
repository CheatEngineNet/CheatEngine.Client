using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable terminal builder for an AOB scan that returns its first match, if any.</summary>
public readonly record struct AobFirstMatchBuilder
{
	private readonly AobScanRequest _request;
	private readonly IPatternScanner? _scanner;

	internal AobFirstMatchBuilder(IPatternScanner scanner, AobScanRequest request)
	{
		_scanner = scanner;
		_request = request;
	}

	/// <summary>Runs the scan and returns its first match, or <see langword="null" /> when no match exists.</summary>
	/// <param name="cancellationToken">Cancels before the scan reaches Cheat Engine.</param>
	/// <returns>The first copied target address, or <see langword="null" />.</returns>
	/// <exception cref="CheatEngineOperationException">The scan operation failed.</exception>
	public Address? Execute(CancellationToken cancellationToken = default)
	{
		if (TryExecute(out Address? address, out CheatEngineFailure failure, cancellationToken))
		{
			return address;
		}

		throw new CheatEngineOperationException(failure);
	}

	/// <summary>Runs the scan and attempts to return its first match.</summary>
	/// <param name="address">The first target address, or <see langword="null" /> when no match exists.</param>
	/// <param name="failure">The scan failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the scan reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when the scan ran successfully, including a no-match result.</returns>
	public bool TryExecute(out Address? address, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (!RequireScanner().TryScan(_request, out AobScanResult result, out failure, cancellationToken))
		{
			address = null;
			return false;
		}

		if (result.Matches.Length > _request.MaximumResults)
		{
			address = null;
			failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, "Aob.FirstOrNone",
				"The pattern scanner returned more matches than the first-match materialization limit.");
			return false;
		}

		address = result.Matches.Length == 0 ? null : result.Matches[0];
		failure = default;
		return true;
	}

	private IPatternScanner RequireScanner()
	{
		return _scanner ?? throw new InvalidOperationException(
			"This AOB terminal builder has no bound pattern scanner. Create it through Aob(pattern).");
	}
}
