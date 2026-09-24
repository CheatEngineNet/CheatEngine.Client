using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Domains.ValueScanning;

/// <summary>Internal boundary that creates CheatEngine.SDK value-scan sessions; Core tests replace it with doubles.</summary>
/// <remarks>Every member runs on Cheat Engine's main thread, inside a dispatched callback.</remarks>
internal interface IValueScanPort
{
	/// <summary>Creates a scanner and its found-list child for Cheat Engine's selected target.</summary>
	/// <param name="session">The session, only when the returned status is <see cref="MemoryScanCreationStatus.Success" />.</param>
	/// <returns>The factual creation status of <c>MemoryScanSessions.TryCreateWithOutcome</c>.</returns>
	public MemoryScanCreationStatus TryCreate(out IValueScanSessionHandle? session);
}

/// <summary>
///     Internal view of one CheatEngine.SDK <c>MemoryScanSession</c>: the stable members only, never the raw
///     <c>Scanner</c> or <c>Results</c> handles (CESDK1001) nor the experimental timed wait and stop (CESDK5010).
/// </summary>
/// <remarks>Every member runs on Cheat Engine's main thread, inside a dispatched callback.</remarks>
internal interface IValueScanSessionHandle
{
	/// <summary>Gets the SDK session state.</summary>
	public MemoryScanState State
	{
		get;
	}

	/// <summary>Gets why the SDK invalidated the session, or <see cref="MemoryScanInvalidationReason.None" />.</summary>
	public MemoryScanInvalidationReason InvalidationReason
	{
		get;
	}

	/// <summary>Gets how cancellation met the last cancellable SDK operation.</summary>
	public MemoryScanCancellationMilestone LastCancellationMilestone
	{
		get;
	}

	/// <summary>Reads the number of initialized results; throws when the SDK refuses or Cheat Engine fails.</summary>
	public ulong ReadResultCount();

	/// <summary>Starts a first scan (<c>StartFirstScanCancellable</c>).</summary>
	public void StartFirstScan(in FirstScanRequest request, CancellationToken cancellationToken);

	/// <summary>Starts a next scan (<c>StartNextScanCancellable</c>).</summary>
	public void StartNextScan(in NextScanRequest request, CancellationToken cancellationToken);

	/// <summary>Waits for the running scan and initializes its results (<c>WaitForCompletionCancellable</c>).</summary>
	public void WaitForCompletion(CancellationToken cancellationToken);

	/// <summary>Clears the results (<c>ResetCancellable</c>).</summary>
	public void Reset(CancellationToken cancellationToken);

	/// <summary>Copies one bounded page of results (<c>TryCopyResultsPageCancellable</c>).</summary>
	public MemoryScanMaterializationStatus TryCopyResultsPage(int firstResultIndex,
		Span<MemoryScanResult> destination, out ulong totalCount, out int written, CancellationToken cancellationToken);

	/// <summary>Copies Cheat Engine's bounded error text of the scan, when it has one.</summary>
	public bool TryGetHostErrorText([NotNullWhen(true)] out string? text, out bool truncated);

	/// <summary>Releases the found list, then the scanner, once (<c>ReleaseWithOutcome</c>); never throws.</summary>
	public ValueScanReleaseStatuses Release();
}

/// <summary>The SDK release status of each owner of a value-scan session, and of the stop of a running scan.</summary>
/// <param name="FoundList">The status of the found-list child, released first.</param>
/// <param name="MemScan">The status of the scanner parent.</param>
/// <param name="Termination">How a scan that may still have been running was stopped before the release.</param>
internal readonly record struct ValueScanReleaseStatuses(
	TargetReleaseStatus FoundList,
	TargetReleaseStatus MemScan,
	MemoryScanTerminationStatus Termination);
