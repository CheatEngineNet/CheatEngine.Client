using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Scanning;

/// <summary>An immutable page of copied value-scan results.</summary>
/// <remarks>
///     A page is copied in full or not at all: a read that fails publishes no page, never a prefix. A scan without
///     results reads as an empty page whose <see cref="ResultCount" /> is zero.
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly struct ValueScanPage
{
	private readonly ImmutableArray<ValueScanMatch> _matches;

	/// <summary>Creates a value-scan result page.</summary>
	/// <param name="startIndex">The zero-based index of the first match of the page.</param>
	/// <param name="resultCount">The number of results Cheat Engine reported when the page was copied.</param>
	/// <param name="matches">The copied matches; a <see langword="default" /> array is read as empty.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="startIndex" /> is negative.</exception>
	public ValueScanPage(long startIndex, ulong resultCount, ImmutableArray<ValueScanMatch> matches)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
		StartIndex = startIndex;
		ResultCount = resultCount;
		_matches = matches.IsDefault ? [] : matches;
	}

	/// <summary>Gets the zero-based index of the first match of the page.</summary>
	public long StartIndex
	{
		get;
	}

	/// <summary>Gets the number of results Cheat Engine reported when the page was copied.</summary>
	public ulong ResultCount
	{
		get;
	}

	/// <summary>Gets the copied matches, in Cheat Engine's result order.</summary>
	/// <remarks>Empty for the <see langword="default" /> value, never a default array.</remarks>
	public ImmutableArray<ValueScanMatch> Matches => _matches.IsDefault ? [] : _matches;

	/// <summary>Gets the index that follows the last match of the page: the start of the next page.</summary>
	public long NextStartIndex => StartIndex + Matches.Length;

	/// <summary>Gets whether results follow this page.</summary>
	public bool HasMore => (ulong) NextStartIndex < ResultCount;
}
