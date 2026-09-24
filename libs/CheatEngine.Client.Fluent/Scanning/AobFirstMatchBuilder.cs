using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>
///     An immutable terminal builder for an AOB scan that returns the first copied match in Cheat Engine's result-list
///     order, if any.
/// </summary>
/// <remarks>
///     <para>
///         Core copies at most one post-filtered address from Cheat Engine's exhaustive result list. The limit bounds only
///         that copy: it never stops Cheat Engine early and is never backed by a bounded or "first found" scan. The
///         returned address is the first element in Cheat Engine's result-list order, which Cheat Engine does not
///         specify: it is not guaranteed to be the lowest address or the first logical region.
///     </para>
///     <para>
///         <see langword="null" /> means Cheat Engine returned a result list without any post-filtered match. A scan
///         that finds nothing is usually reported as <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> (no
///         result list: zero matches or a host failure, indistinguishable on this scan route), never converted to
///         <see langword="null" />.
///     </para>
/// </remarks>
public readonly record struct AobFirstMatchBuilder
{
	private readonly AobScanRequest _request;
	private readonly IPatternScanner? _scanner;

	internal AobFirstMatchBuilder(IPatternScanner scanner, AobScanRequest request)
	{
		_scanner = scanner;
		_request = request;
	}

	/// <summary>
	///     Runs the scan and returns its first copied match, or <see langword="null" /> when the returned list held no
	///     post-filtered match.
	/// </summary>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The first copied target address, or <see langword="null" />.</returns>
	/// <exception cref="CheatEngineOperationException">
	///     The scan operation failed, including the indeterminate "no result list" outcome.
	/// </exception>
	public Address? Execute(CancellationToken cancellationToken = default)
	{
		if (TryExecute(out Address? address, out CheatEngineFailure failure, cancellationToken))
		{
			return address;
		}

		throw new CheatEngineOperationException(failure);
	}

	/// <summary>Runs the scan and attempts to return its first match.</summary>
	/// <param name="address">
	///     The first copied target address, or <see langword="null" /> when the returned list held no post-filtered match.
	/// </param>
	/// <param name="failure">The scan failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>
	///     <see langword="true" /> when Cheat Engine returned a result list, including an empty or fully post-filtered one;
	///     <see langword="false" /> for every failure, including <see cref="CheatEngineFailureKind.IndeterminateHostResult" />.
	/// </returns>
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
