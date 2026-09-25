using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable terminal builder for an AOB scan that must have exactly one match.</summary>
/// <remarks>
///     <para>
///         Create it with <see cref="AobScanBuilder.RequireSingle" />. It is a plain value that declares no
///         <c>Equals</c>, <c>GetHashCode</c>, <c>ToString</c> or equality operators (only those inherited from
///         <see cref="ValueType" />): compare the requests it runs, not the builders. Its only constructor is the implicit
///         parameterless one, which yields the <see langword="default" /> value: that value has no pattern scanner, and
///         its terminals throw <see cref="InvalidOperationException" />.
///     </para>
///     <para>
///         Uniqueness is proven from an exhaustive scan (the global scan, or the exhaustive bounded scan of a module or
///         range) of which Core copies up to two matches. Two copied matches are reported as
///         <see cref="CheatEngineFailureKind.AmbiguousMatch" />: a second match was observed. One copied match counts as
///         unique only when every row Cheat Engine returned was read and the copy is not truncated. Otherwise whether a
///         second match lies inside the request is unknown (for example when the bounded route filled its destination
///         with rows that lie outside the request and left rows unread), and the terminal reports
///         <see cref="CheatEngineFailureKind.IndeterminateHostResult" />, never
///         <see cref="CheatEngineFailureKind.AmbiguousMatch" />. This operation is never backed by a "unique" or "first
///         found" scan, and a copy limit of one is never treated as proof of uniqueness.
///     </para>
///     <para>
///         <see cref="CheatEngineFailureKind.NotFound" /> is a factual zero: the scan succeeded, every row Cheat Engine
///         returned was read, and none lay inside the request (the bounded route's empty in-bounds result, a global result
///         list whose rows all lie outside the module or range, or an empty list that a global scan does return). A
///         global scan for which Cheat Engine returns no result list is reported as
///         <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> (on Cheat Engine 7.7 <c>AOBScan</c> returns
///         <c>nil</c> for zero matches and for some host failures alike), and so is a copy that did not read every row:
///         neither is ever <see cref="CheatEngineFailureKind.NotFound" />.
///     </para>
/// </remarks>
public readonly struct AobSingleMatchBuilder
{
	private const string Operation = "Patterns.Scan";
	private readonly AobScanRequest _request;
	private readonly IPatternScanner? _scanner;

	internal AobSingleMatchBuilder(IPatternScanner scanner, AobScanRequest request)
	{
		_scanner = scanner;
		_request = request;
	}

	/// <summary>Runs the scan and returns its sole match.</summary>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The sole target address.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no pattern scanner.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The scan failed (including the indeterminate <c>nil</c> outcome), it proved that no match lies inside the
	///     request, it copied two matches, or it copied one match without proving that no second one exists.
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
	public Address Execute(CancellationToken cancellationToken = default)
	{
		if (TryExecute(out Address address, out CheatEngineFailure failure, cancellationToken))
		{
			return address;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	/// <summary>Runs the scan and attempts to return its sole match.</summary>
	/// <param name="address">The sole target address when the method returns <see langword="true" />.</param>
	/// <param name="failure">The scan or cardinality failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>
	///     <see langword="true" /> when the scan read every row Cheat Engine returned and exactly one lay inside the
	///     request.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no pattern scanner.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The Client activation that owns the scanner has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryExecute(out Address address, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		address = default;
		if (!AobTerminal.TryScan(RequireScanner(), _request, Operation, out AobScanResult result,
				out bool isExhaustive, out failure, cancellationToken))
		{
			return false;
		}

		if (result.Matches.IsEmpty)
		{
			failure = AobTerminal.Fail(CheatEngineFailureKind.NotFound, Operation, "The AOB scan did not find a match.");
			return false;
		}

		if (result.Matches.Length > 1)
		{
			failure = AobTerminal.Fail(CheatEngineFailureKind.AmbiguousMatch, Operation,
				"The AOB scan found more than one match.");
			return false;
		}

		// With a copy limit of two, one copied match never proves a second one: a truncated single-match copy (the
		// bounded route's full destination of dropped rows) or unread rows leave uniqueness unknown.
		if (result.IsTruncated || !isExhaustive)
		{
			failure = AobTerminal.Unread(Operation);
			return false;
		}

		address = result.Matches[0];
		return true;
	}

	private IPatternScanner RequireScanner()
	{
		return _scanner ?? throw new InvalidOperationException(
			"This AOB terminal builder is a default value without a pattern scanner. Select it from " +
			"scanner.Aob(pattern), for example client.Patterns.Aob(pattern).RequireSingle().");
	}
}
