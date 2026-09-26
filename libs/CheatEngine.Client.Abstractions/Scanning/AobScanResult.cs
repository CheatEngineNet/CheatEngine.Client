using System.Collections.Immutable;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>A copied, handle-free AOB scan result.</summary>
public readonly struct AobScanResult
{
	private readonly ImmutableArray<Address> _matches;

	/// <summary>Creates a copied AOB scan result.</summary>
	/// <param name="matches">The copied match addresses; a default array is empty.</param>
	/// <param name="isTruncated">Whether the copy is not proven complete (see <see cref="IsTruncated" />).</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="isTruncated" /> is <see langword="true" /> and <paramref name="matches" /> is empty.
	/// </exception>
	public AobScanResult(ImmutableArray<Address> matches, bool isTruncated)
	{
		_matches = matches.IsDefault ? ImmutableArray<Address>.Empty : matches;
		if (isTruncated && _matches.IsEmpty)
		{
			throw new ArgumentException("A truncated AOB result must retain at least one copied match.",
				nameof(matches));
		}

		IsTruncated = isTruncated;
	}

	/// <summary>Gets the materialized target addresses.</summary>
	/// <remarks>Empty for the <see langword="default" /> value, never a default array.</remarks>
	public ImmutableArray<Address> Matches => _matches.IsDefault ? ImmutableArray<Address>.Empty : _matches;

	/// <summary>
	///     Gets whether the copy is not proven complete: more matches inside the request may exist beyond the
	///     copied ones, or rows Cheat Engine returned were left unread.
	/// </summary>
	/// <remarks>
	///     <para>
	///         Every route sets it when it found one more match inside the request than it copied: the copy
	///         stopped at <see cref="AobScanRequest.MaximumResults" /> or at the Client's cap of 65,535
	///         addresses, whichever is lower, and further matches exist.
	///     </para>
	///     <para>
	///         The bounded route (<see cref="PatternScanScope.HostBoundedRange" />) also sets it when its
	///         destination filled up with rows outside the request, for example matches that straddle the module
	///         end, while Cheat Engine returned more rows: those rows were not read
	///         (<see cref="PatternScanMetrics.UnreadHostRowCount" />), so whether they hold further matches is
	///         unknown. The global route reads every row of the same result, so it can report the same matches as
	///         complete.
	///     </para>
	///     <para>
	///         <see langword="false" /> means that every match inside the request was copied. Never read a
	///         truncated result as a count, and never read its lack of a second match as uniqueness.
	///     </para>
	/// </remarks>
	public bool IsTruncated
	{
		get;
	}
}
