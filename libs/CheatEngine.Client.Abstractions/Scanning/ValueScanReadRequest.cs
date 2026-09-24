using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Scanning;

/// <summary>A bounded, zero-based read of the current value-scan results.</summary>
/// <remarks>
///     Cheat Engine addresses its result list with a 32-bit index: a <see cref="StartIndex" /> above
///     <see cref="int.MaxValue" /> is refused with <see cref="Results.CheatEngineFailureKind.ResultLimitExceeded" />
///     before any Cheat Engine call. One read copies at most <see cref="MaximumCount" /> results and never more than the
///     Client's page limit (1024 results); read the next page from <see cref="ValueScanPage.NextStartIndex" />.
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct ValueScanReadRequest
{
	/// <summary>Creates a bounded result read.</summary>
	/// <param name="startIndex">The zero-based index of the first result to copy.</param>
	/// <param name="maximumCount">The positive maximum number of results to copy.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="startIndex" /> is negative, or <paramref name="maximumCount" /> is zero or negative.
	/// </exception>
	public ValueScanReadRequest(long startIndex, int maximumCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
		StartIndex = startIndex;
		MaximumCount = maximumCount;
	}

	/// <summary>Gets the zero-based index of the first result to copy.</summary>
	public long StartIndex
	{
		get;
	}

	/// <summary>Gets the maximum number of results to copy.</summary>
	public int MaximumCount
	{
		get;
	}
}
