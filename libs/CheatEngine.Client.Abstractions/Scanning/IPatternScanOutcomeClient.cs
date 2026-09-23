namespace CheatEngine.Client.Scanning;

/// <summary>Runs AOB scans and returns their detailed outcome, including host and copy metrics.</summary>
/// <remarks>
///     This companion contract is intentionally separate from <see cref="IPatternScanner" /> so existing implementations
///     remain source-compatible. It exposes what the boolean <see cref="IPatternScanner.TryScan" /> cannot: how many
///     entries Cheat Engine returned, how many Core examined, filtered out and copied, which part of the target Cheat
///     Engine scanned, and the Cheat Engine scan time separately from the Client copy time.
/// </remarks>
public interface IPatternScanOutcomeClient
{
	/// <summary>Runs one scan and returns its detailed outcome.</summary>
	/// <param name="request">The scan request.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine scan that has
	///     already started (see <see cref="Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The detailed outcome. Expected failures are returned in <see cref="PatternScanOutcome.Cause" />.</returns>
	/// <exception cref="Results.CheatEngineActivationExpiredException">The client activation has expired.</exception>
	/// <exception cref="Results.CheatEngineClientLifecycleException">The client activation is stopping.</exception>
	public PatternScanOutcome ScanDetailed(AobScanRequest request, CancellationToken cancellationToken = default);
}
