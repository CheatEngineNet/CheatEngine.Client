using System.Collections.Immutable;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable page of copied value-scan results.</summary>
public readonly record struct ValueScanPage
{
	/// <summary>Creates a value-scan result page.</summary>
	public ValueScanPage(ulong totalCount, ImmutableArray<ValueScanMatch> matches)
	{
		TotalCount = totalCount;
		Matches = matches.IsDefault ? ImmutableArray<ValueScanMatch>.Empty : matches;
	}

	/// <summary>Gets the total CE result count observed while reading this page.</summary>
	public ulong TotalCount
	{
		get;
	}

	/// <summary>Gets the copied result matches.</summary>
	public ImmutableArray<ValueScanMatch> Matches
	{
		get;
	}
}
