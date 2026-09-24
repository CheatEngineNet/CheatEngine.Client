using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Core.Domains.ValueScanning;

/// <summary>Production adapter over <c>MemoryScanSessions</c> and <c>MemoryScanSession</c> of CheatEngine.SDK 2.0.0.</summary>
/// <remarks>
///     <para>
///         Creation goes through <see cref="MemoryScanSessions.TryCreateWithOutcome" />, the only SDK path that turns the
///         <c>createMemScan</c> and <c>createFoundList</c> results into owners, for a qualified target only, and rolls the
///         child back before the parent when creation fails. The adapter keeps the SDK session as the single owner of both
///         objects; it never reads the raw <c>Scanner</c> or <c>Results</c> handles and never calls the experimental
///         <c>TryWaitForCompletion</c> or <c>TryTerminateScan</c>.
///     </para>
///     <para>
///         Only a hosted Cheat Engine can reach this adapter: it is excluded from the coverage metric, and Core tests
///         exercise the value-scan domain through <see cref="IValueScanPort" /> doubles.
///     </para>
/// </remarks>
internal sealed class SdkValueScanPort : IValueScanPort
{
	private SdkValueScanPort()
	{
	}

	/// <summary>Gets the production adapter.</summary>
	internal static SdkValueScanPort Instance
	{
		get;
	} = new();

	public MemoryScanCreationStatus TryCreate(out IValueScanSessionHandle? session)
	{
		MemoryScanCreationOutcome outcome = MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? created);
		if (outcome.Status == MemoryScanCreationStatus.Success && created is not null)
		{
			session = new SdkValueScanSessionHandle(created);
			return outcome.Status;
		}

		// The SDK publishes a session only with Success; release one that a contract break would publish anyway rather
		// than leak its Cheat Engine objects.
		_ = created?.ReleaseWithOutcome();
		session = null;
		return outcome.Status;
	}

	private sealed class SdkValueScanSessionHandle(MemoryScanSession session) : IValueScanSessionHandle
	{
		private readonly MemoryScanSession _session = session ?? throw new ArgumentNullException(nameof(session));

		public MemoryScanState State => _session.State;

		public MemoryScanInvalidationReason InvalidationReason => _session.InvalidationReason;

		public MemoryScanCancellationMilestone LastCancellationMilestone => _session.LastCancellationMilestone;

		public ulong ReadResultCount()
		{
			return _session.ResultCount;
		}

		public void StartFirstScan(in FirstScanRequest request, CancellationToken cancellationToken)
		{
			_session.StartFirstScanCancellable(in request, cancellationToken);
		}

		public void StartNextScan(in NextScanRequest request, CancellationToken cancellationToken)
		{
			_session.StartNextScanCancellable(in request, cancellationToken);
		}

		public void WaitForCompletion(CancellationToken cancellationToken)
		{
			_session.WaitForCompletionCancellable(cancellationToken);
		}

		public void Reset(CancellationToken cancellationToken)
		{
			_session.ResetCancellable(cancellationToken);
		}

		public MemoryScanMaterializationStatus TryCopyResultsPage(int firstResultIndex,
			Span<MemoryScanResult> destination, out ulong totalCount, out int written,
			CancellationToken cancellationToken)
		{
			return _session.TryCopyResultsPageCancellable(firstResultIndex, destination, out totalCount, out written,
				cancellationToken);
		}

		public bool TryGetHostErrorText([NotNullWhen(true)] out string? text, out bool truncated)
		{
			return _session.TryGetHostErrorText(out text, out truncated);
		}

		public ValueScanReleaseStatuses Release()
		{
			MemoryScanReleaseOutcome outcome = _session.ReleaseWithOutcome();
			return new ValueScanReleaseStatuses(outcome.FoundList.Status, outcome.MemScan.Status, outcome.Termination);
		}
	}
}
