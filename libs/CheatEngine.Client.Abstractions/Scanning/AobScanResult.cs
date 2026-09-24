using System.Collections.Immutable;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>A copied, handle-free AOB scan result.</summary>
public readonly record struct AobScanResult
{
	/// <summary>Creates a copied AOB scan result.</summary>
	public AobScanResult(ImmutableArray<Address> matches, bool isTruncated)
	{
		Matches = matches.IsDefault ? ImmutableArray<Address>.Empty : matches;
		if (isTruncated && Matches.IsEmpty)
		{
			throw new ArgumentException("A truncated AOB result must retain at least one copied match.",
				nameof(matches));
		}

		IsTruncated = isTruncated;
	}

	/// <summary>Gets the materialized target addresses.</summary>
	public ImmutableArray<Address> Matches
	{
		get;
	}

	/// <summary>
	///     Gets whether further matches inside the request exist beyond the copied ones: the copy stopped at
	///     <see cref="AobScanRequest.MaximumResults" /> or at the Client's cap of 65,535 addresses, whichever is lower.
	/// </summary>
	public bool IsTruncated
	{
		get;
	}
}
