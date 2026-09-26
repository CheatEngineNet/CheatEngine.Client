using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>
///     An immutable terminal builder for an AOB scan that returns the first copied match in Cheat Engine's result-list
///     order, if any.
/// </summary>
/// <remarks>
///     <para>
///         Create it with <see cref="AobScanBuilder.FirstOrNone" />. It is a plain value that declares no
///         <c>Equals</c>, <c>GetHashCode</c>, <c>ToString</c> or equality operators (only those inherited from
///         <see cref="ValueType" />): compare the requests it runs, not the builders. Its only constructor is the implicit
///         parameterless one, which yields the <see langword="default" /> value: that value has no pattern scanner, and
///         its terminals throw <see cref="InvalidOperationException" />.
///     </para>
///     <para>
///         Core copies at most one address from an exhaustive scan (the global scan, or the exhaustive bounded scan of a
///         module or range). The limit bounds only that copy: it never stops Cheat Engine early and is never backed by a
///         "first found" scan. The returned address is the first element in Cheat Engine's result-list order, which Cheat
///         Engine does not specify: it is not guaranteed to be the lowest address or the first logical region.
///     </para>
///     <para>
///         <see langword="null" /> is a factual zero: the scan succeeded, every row Cheat Engine returned was read, and
///         none lay inside the request. That is the bounded route's empty in-bounds result, a global result list whose
///         rows all lie outside the module or range, or an empty list that a global scan does return. A global scan for
///         which Cheat Engine returns no result list is reported as
///         <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> (on Cheat Engine 7.7 <c>AOBScan</c> returns
///         <c>nil</c> for zero matches and for some host failures alike), and so is an empty copy that did not read every
///         row: neither is ever converted to <see langword="null" />.
///     </para>
/// </remarks>
public readonly struct AobFirstMatchBuilder
{
	private const string Operation = "Patterns.Scan";
	private readonly AobScanRequest _request;
	private readonly IPatternScanner? _scanner;

	internal AobFirstMatchBuilder(IPatternScanner scanner, AobScanRequest request)
	{
		_scanner = scanner;
		_request = request;
	}

	/// <summary>
	///     Runs the scan and returns its first copied match, or <see langword="null" /> when the scan read every row Cheat
	///     Engine returned and none lay inside the request.
	/// </summary>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The first copied target address, or <see langword="null" /> for a factual zero.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no pattern scanner.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The scan operation failed, including the indeterminate <c>nil</c> outcome.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     <paramref name="cancellationToken" /> was observed before the scan started or while Core copied its result.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The Client activation that owns the scanner has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback, or the scan failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <remarks>
	///     Failures are thrown through <see cref="CheatEngineFailure.Throw(CancellationToken)" />, so the exception type
	///     follows <see cref="CheatEngineFailure.Kind" />; <see cref="TryExecute" /> returns the same failure instead.
	/// </remarks>
	public Address? Execute(CancellationToken cancellationToken = default)
	{
		if (TryExecute(out Address? address, out CheatEngineFailure failure, cancellationToken))
		{
			return address;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	/// <summary>Runs the scan and attempts to return its first match.</summary>
	/// <param name="address">
	///     The first copied target address, or <see langword="null" /> when the scan read every row Cheat Engine returned
	///     and none lay inside the request.
	/// </param>
	/// <param name="failure">The scan failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>
	///     <see langword="true" /> when the scan copied a match or proved a factual zero; <see langword="false" /> for every
	///     failure, including <see cref="CheatEngineFailureKind.IndeterminateHostResult" />.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no pattern scanner.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The Client activation that owns the scanner has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryExecute(out Address? address, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (!AobTerminal.TryScan(RequireScanner(), _request, Operation, out AobScanResult result, out _, out failure,
				cancellationToken))
		{
			address = null;
			return false;
		}

		address = result.Matches.IsEmpty ? null : result.Matches[0];
		return true;
	}

	private IPatternScanner RequireScanner()
	{
		return _scanner ?? throw new InvalidOperationException(
			"This AOB terminal builder is a default value without a pattern scanner. Select it from " +
			"scanner.Aob(pattern), for example client.Patterns.Aob(pattern).FirstOrNone().");
	}
}
