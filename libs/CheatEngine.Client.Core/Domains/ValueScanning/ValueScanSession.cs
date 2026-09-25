using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Core.Domains.ValueScanning;

/// <summary>One value-scan session: a lease over one CheatEngine.SDK <c>MemoryScanSession</c>.</summary>
/// <remarks>
///     <para>
///         Every operation runs on Cheat Engine's main thread through the activation dispatcher and touches the SDK session
///         only there. A first or next scan starts Cheat Engine's scan and waits for it in the same callback
///         (<c>Start*Cancellable</c>, then <c>WaitForCompletionCancellable</c>), so the results are initialized before the
///         call returns. The Client state is the SDK state copied after each operation, so reading it never touches the
///         SDK session.
///     </para>
///     <para>
///         The session is a <see cref="HostResourceLease" /> registered with the activation and with its target selection:
///         the release runs <c>ReleaseWithOutcome</c> (found list, then scanner) on the main thread, when the application
///         releases it, when the Client observes that Cheat Engine selected another process, or before CheatEngine.SDK
///         detaches at deactivation. The release that follows a target change is refused by CheatEngine.SDK without any
///         Cheat Engine call, which consumes both owners and leaves the two objects in Cheat Engine.
///     </para>
/// </remarks>
internal sealed class ValueScanSession : HostResourceLease, IValueScanSession
{
	/// <summary>The public operation name of the release.</summary>
	internal const string ReleaseOperation = "ValueScans.Release";

	/// <summary>The public operation name of a first scan.</summary>
	internal const string FirstScanOperation = "ValueScans.FirstScan";

	/// <summary>The public operation name of a next scan.</summary>
	internal const string NextScanOperation = "ValueScans.NextScan";

	/// <summary>The public operation name of a reset.</summary>
	internal const string ResetOperation = "ValueScans.Reset";

	/// <summary>The public operation name of a result count.</summary>
	internal const string ResultCountOperation = "ValueScans.GetResultCount";

	/// <summary>The public operation name of a result read.</summary>
	internal const string ReadOperation = "ValueScans.Read";

	private const string ScanNotAwaitedMessage =
		"The operation was cancelled after Cheat Engine started the scan and before the Client waited for it: the " +
		"session keeps scanning until it is released.";

	private readonly SdkMainThreadDispatcher _dispatcher;
	private readonly IValueScanSessionHandle _handle;
	private int _invalidation = (int) ValueScanInvalidationKind.None;
	private int _state = (int) ValueScanSessionState.Created;

	// The value type of the scan whose results are ready, read and written on the main thread only.
	private ValueScanValueType? _valueType;

	/// <summary>Creates the lease of a created SDK session; the caller registers it.</summary>
	/// <param name="dispatcher">The activation dispatcher.</param>
	/// <param name="handle">The SDK session, which this lease owns from now on.</param>
	/// <param name="selectionEpoch">The target-selection epoch of the process the session was created in.</param>
	internal ValueScanSession(SdkMainThreadDispatcher dispatcher, IValueScanSessionHandle handle, long selectionEpoch)
		: base(ReleaseOperation, dispatcher, dispatcher?.Lifetime.Diagnostics)
	{
		_dispatcher = dispatcher!;
		_handle = handle ?? throw new ArgumentNullException(nameof(handle));
		SelectionEpoch = selectionEpoch;
	}

	public long SelectionEpoch
	{
		get;
	}

	public ValueScanSessionState State =>
		IsReleased ? ValueScanSessionState.Closed : (ValueScanSessionState) Volatile.Read(ref _state);

	public ValueScanInvalidationKind Invalidation => (ValueScanInvalidationKind) Volatile.Read(ref _invalidation);

	public bool TryFirstScan(ValueScanFirstRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		// An ended or stopping activation throws before a refusal is reported, never the reverse.
		_dispatcher.Lifetime.ThrowIfDispatchAllowed(FirstScanOperation);
		if (!ValueScanRequests.TryCreateFirst(request, FirstScanOperation, out FirstScanRequest sdkRequest, out failure))
		{
			return false;
		}

		return TryRun(FirstScanOperation,
			token => FirstScanOnMainThread(sdkRequest, request.ValueType, token), out failure, cancellationToken);
	}

	public void FirstScan(ValueScanFirstRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryFirstScan(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}
	}

	public bool TryNextScan(ValueScanNextRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryRun(NextScanOperation, token => NextScanOnMainThread(request, token), out failure,
			cancellationToken);
	}

	public void NextScan(ValueScanNextRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryNextScan(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}
	}

	public bool TryReset(out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		return TryRun(ResetOperation, ResetOnMainThread, out failure, cancellationToken);
	}

	public void Reset(CancellationToken cancellationToken = default)
	{
		if (!TryReset(out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}
	}

	public bool TryGetResultCount(out ulong resultCount, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ulong count = 0;
		bool succeeded = TryRun(ResultCountOperation, token => CountOnMainThread(token, out count), out failure,
			cancellationToken);
		resultCount = succeeded ? count : 0;
		return succeeded;
	}

	public ulong GetResultCount(CancellationToken cancellationToken = default)
	{
		if (TryGetResultCount(out ulong resultCount, out CheatEngineFailure failure, cancellationToken))
		{
			return resultCount;
		}

		failure.Throw(cancellationToken);
		return 0;
	}

	public bool TryRead(ValueScanReadRequest request, out ValueScanPage page, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		page = default;
		// An ended or stopping activation throws before a refusal is reported, never the reverse.
		_dispatcher.Lifetime.ThrowIfDispatchAllowed(ReadOperation);
		if (request.MaximumCount <= 0)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, ReadOperation,
				"A value-scan read requires a positive maximum count.", null, CheatEngineHostEffect.NotStarted);
			return false;
		}

		if (request.StartIndex > int.MaxValue)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.ResultLimitExceeded, ReadOperation,
				"Cheat Engine addresses scan results with a 32-bit index; the page starts beyond it.", null,
				CheatEngineHostEffect.NotStarted);
			return false;
		}

		ValueScanPage read = default;
		bool succeeded = TryRun(ReadOperation, token => ReadOnMainThread(request, token, out read), out failure,
			cancellationToken);
		page = succeeded ? read : default;
		return succeeded;
	}

	public ValueScanPage Read(ValueScanReadRequest request, CancellationToken cancellationToken = default)
	{
		if (TryRead(request, out ValueScanPage page, out CheatEngineFailure failure, cancellationToken))
		{
			return page;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	protected override LeaseReleaseOutcome ReleaseOnMainThread()
	{
		try
		{
			return ValueScanMapping.FromRelease(_handle.Release());
		}
		finally
		{
			Refresh();
		}
	}

	/// <summary>
	///     Dispatches one operation: <see langword="true" /> when it succeeded, otherwise <see langword="false" /> with its
	///     failure.
	/// </summary>
	/// <remarks>
	///     A cancellation observed before dispatch, and a session that was released, are refused without any SDK call. The
	///     Client state is copied from the SDK session after every dispatched operation, whatever its result.
	/// </remarks>
	private bool TryRun(string operation, Func<CancellationToken, CheatEngineFailure?> work,
		out CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		// An ended or stopping activation throws before a cancellation is reported, never the reverse.
		_dispatcher.Lifetime.ThrowIfDispatchAllowed(operation);
		if (cancellationToken.IsCancellationRequested)
		{
			failure = CancellationMapping.BeforeNativeCall(operation);
			return false;
		}

		if (!_dispatcher.TryInvoke(() => RunOnMainThread(operation, work, cancellationToken),
				out CheatEngineFailure? outcome, out failure, cancellationToken))
		{
			return false;
		}

		failure = outcome ?? default;
		return outcome is null;
	}

	private CheatEngineFailure? RunOnMainThread(string operation, Func<CancellationToken, CheatEngineFailure?> work,
		CancellationToken cancellationToken)
	{
		if (IsReleased || _handle.State == MemoryScanState.Disposed)
		{
			Refresh();
			return ValueScanMapping.Released(operation, LastReleaseOutcome);
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return CancellationMapping.BeforeNativeCall(operation);
		}

		try
		{
			return work(cancellationToken);
		}
		finally
		{
			Refresh();
		}
	}

	private CheatEngineFailure? FirstScanOnMainThread(FirstScanRequest request, ValueScanValueType valueType,
		CancellationToken cancellationToken)
	{
		try
		{
			_handle.StartFirstScan(in request, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return ValueScanMapping.Cancelled(FirstScanOperation, _handle.LastCancellationMilestone);
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			return StartFailed(FirstScanOperation, fault);
		}

		return WaitForResults(FirstScanOperation, valueType, cancellationToken);
	}

	private CheatEngineFailure? NextScanOnMainThread(ValueScanNextRequest request,
		CancellationToken cancellationToken)
	{
		if (!ValueScanRequests.TryCreateNext(request, _valueType, NextScanOperation, out NextScanRequest sdkRequest,
				out CheatEngineFailure refusal))
		{
			return refusal;
		}

		try
		{
			_handle.StartNextScan(in sdkRequest, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return ValueScanMapping.Cancelled(NextScanOperation, _handle.LastCancellationMilestone);
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			return StartFailed(NextScanOperation, fault);
		}

		return WaitForResults(NextScanOperation, _valueType ?? request.Value?.ValueType ?? ValueScanValueType.Integer32,
			cancellationToken);
	}

	/// <summary>Waits for a started scan; from here on Cheat Engine's scan has started, whatever happens.</summary>
	private CheatEngineFailure? WaitForResults(string operation, ValueScanValueType valueType,
		CancellationToken cancellationToken)
	{
		try
		{
			_handle.WaitForCompletion(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return CancellationMapping.BetweenNativeCalls(operation, ScanNotAwaitedMessage);
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			return WithHostErrorText(SdkBoundary.Translate(operation, fault, CheatEngineHostEffect.Started,
				_dispatcher.Lifetime));
		}

		_valueType = valueType;
		return cancellationToken.IsCancellationRequested ? CancellationMapping.AfterNativeCall(operation) : null;
	}

	private CheatEngineFailure? ResetOnMainThread(CancellationToken cancellationToken)
	{
		try
		{
			_handle.Reset(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return ValueScanMapping.Cancelled(ResetOperation, _handle.LastCancellationMilestone);
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			return SdkBoundary.Translate(ResetOperation, fault, ValueScanMapping.MutationFaultEffect(fault),
				_dispatcher.Lifetime);
		}

		_valueType = null;
		return cancellationToken.IsCancellationRequested ? CancellationMapping.AfterNativeCall(ResetOperation) : null;
	}

	private CheatEngineFailure? CountOnMainThread(CancellationToken cancellationToken, out ulong count)
	{
		count = 0;
		try
		{
			count = _handle.ReadResultCount();
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			return SdkBoundary.Translate(ResultCountOperation, fault, ValueScanMapping.ReadFaultEffect(fault),
				_dispatcher.Lifetime);
		}

		return cancellationToken.IsCancellationRequested
			? CancellationMapping.AfterNativeCall(ResultCountOperation)
			: null;
	}

	private CheatEngineFailure? ReadOnMainThread(ValueScanReadRequest request, CancellationToken cancellationToken,
		out ValueScanPage page)
	{
		page = default;
		int capacity = Math.Min(request.MaximumCount, ScanResourceLimits.MaximumValueScanPage);
		MemoryScanResult[] buffer = new MemoryScanResult[capacity];
		MemoryScanMaterializationStatus status;
		ulong totalCount;
		int written;
		try
		{
			status = _handle.TryCopyResultsPage((int) request.StartIndex, buffer, out totalCount, out written,
				cancellationToken);
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			return SdkBoundary.Translate(ReadOperation, fault, ValueScanMapping.ReadFaultEffect(fault),
				_dispatcher.Lifetime);
		}

		if (ValueScanMapping.TryGetPageFailure(status, _handle.LastCancellationMilestone, ReadOperation,
				out CheatEngineFailure failure))
		{
			return failure;
		}

		if (status == MemoryScanMaterializationStatus.NoResults)
		{
			page = new ValueScanPage(request.StartIndex, 0, []);
			return cancellationToken.IsCancellationRequested ? CancellationMapping.AfterNativeCall(ReadOperation) : null;
		}

		if (written < 0 || written > capacity || Array.Exists(buffer[..written], static row => row.Value is null))
		{
			return new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, ReadOperation,
				"CheatEngine.SDK reported a copied result page outside its documented shape; no result was copied.", null,
				CheatEngineHostEffect.Completed);
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return CancellationMapping.AfterNativeCall(ReadOperation);
		}

		ValueScanMatch[] matches = new ValueScanMatch[written];
		for (int index = 0; index < written; index++)
		{
			matches[index] = new ValueScanMatch(buffer[index].Address, buffer[index].Value);
		}

		page = new ValueScanPage(request.StartIndex, totalCount, [.. matches]);
		return null;
	}

	private CheatEngineFailure StartFailed(string operation, Exception fault)
	{
		CheatEngineFailure failure = SdkBoundary.Translate(operation, fault, ValueScanMapping.MutationFaultEffect(fault),
			_dispatcher.Lifetime);
		return failure.HostEffect == CheatEngineHostEffect.NotStarted ? failure : WithHostErrorText(failure);
	}

	/// <summary>Appends Cheat Engine's bounded error text of the scan, when the session can still read it.</summary>
	private CheatEngineFailure WithHostErrorText(CheatEngineFailure failure)
	{
		try
		{
			return _handle.TryGetHostErrorText(out string? text, out bool truncated)
				? ValueScanMapping.WithHostErrorText(failure, text, truncated)
				: failure;
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			// The text is a diagnostic fact only: a session that can no longer read it keeps the failure as it is.
			return failure;
		}
	}

	private void Refresh()
	{
		Volatile.Write(ref _state, (int) ValueScanMapping.ToState(_handle.State));
		Volatile.Write(ref _invalidation, (int) ValueScanMapping.ToInvalidation(_handle.InvalidationReason));
	}
}
