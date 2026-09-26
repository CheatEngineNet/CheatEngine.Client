using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.AotProbe;

/// <summary>
///     A pattern scanner behind the Fluent AOB builders that reports one match, read exhaustively, for every request,
///     so that each terminal runs under Native AOT without a Cheat Engine host.
/// </summary>
/// <remarks>The Fluent terminals read <see cref="ScanDetailed" /> only; the other members throw.</remarks>
internal sealed class AotProbePatternScanner : IPatternScanner
{
	/// <summary>Gets the one address every scan reports.</summary>
	internal static Address Match => new(0x7FF6_0000_1000);

	/// <summary>Gets the request of the last scan.</summary>
	internal AobScanRequest LastRequest
	{
		get;
		private set;
	}

	/// <inheritdoc />
	public bool TryScan(AobScanRequest request, out AobScanResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException("The Fluent AOB terminals read the detailed scan outcome only.");
	}

	/// <inheritdoc />
	public AobScanResult Scan(AobScanRequest request, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException("The Fluent AOB terminals read the detailed scan outcome only.");
	}

	/// <inheritdoc />
	public PatternScanOutcome ScanDetailed(AobScanRequest request, CancellationToken cancellationToken = default)
	{
		LastRequest = request;
		PatternScanMetrics metrics = new(PatternScanScope.GlobalHostScan, hostResultCount: 1, examinedCount: 1,
			filteredOutCount: 0, materializedCount: 1, belowStartSkippedCount: 0, atOrAfterStopSkippedCount: 0,
			unreadHostRowCount: 0, inBoundsCountIsExact: true, TimeSpan.Zero, TimeSpan.Zero);
		return new PatternScanOutcome(new AobScanResult([Match], isTruncated: false), null, metrics,
			PatternScanHostOutcomeKind.Matches, PatternScanRouteReason.UnscopedRequest, targetIdentityVerified: false);
	}
}
