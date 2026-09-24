using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

using CheatEngine.Client.Core.Domains.ValueScanning;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>A scripted scan-session factory: returns <see cref="Session" /> when <see cref="Status" /> is a success.</summary>
internal sealed class FakeValueScanPort : IValueScanPort
{
	internal MemoryScanCreationStatus Status
	{
		get;
		set;
	} = MemoryScanCreationStatus.Success;

	internal Exception? Fault
	{
		get;
		set;
	}

	internal Action? DuringCreate
	{
		get;
		set;
	}

	internal FakeValueScanSessionHandle Session
	{
		get;
	} = new();

	internal int Creations
	{
		get;
		private set;
	}

	public MemoryScanCreationStatus TryCreate(out IValueScanSessionHandle? session)
	{
		Creations++;
		DuringCreate?.Invoke();
		if (Fault is { } fault)
		{
			throw fault;
		}

		session = Status == MemoryScanCreationStatus.Success ? Session : null;
		return Status;
	}
}

/// <summary>
///     Emulates the CheatEngine.SDK 2.0.0 <c>MemoryScanSession</c> state machine that the Client relies on: state checks
///     before the Cheat Engine calls, the conservative invalidation around them, cancellation milestones, the refusal of a
///     call made while another member is inside Cheat Engine, and the one deferred release.
/// </summary>
internal sealed class FakeValueScanSessionHandle : IValueScanSessionHandle
{
	private bool _active;
	private ValueScanReleaseStatuses? _finalRelease;
	private bool _releaseDeferred;

	internal List<string> Calls
	{
		get;
	} = [];

	internal List<MemoryScanResult> Results
	{
		get;
	} = [];

	/// <summary>A count that overrides the size of <see cref="Results" />.</summary>
	internal ulong? ReportedCount
	{
		get;
		set;
	}

	/// <summary>A context refusal (changed runtime or target) thrown before any Cheat Engine call.</summary>
	internal MemoryScanException? ContextFault
	{
		get;
		set;
	}

	internal Exception? StartFault
	{
		get;
		set;
	}

	internal Exception? WaitFault
	{
		get;
		set;
	}

	internal Exception? ResetFault
	{
		get;
		set;
	}

	internal Exception? CountFault
	{
		get;
		set;
	}

	internal Exception? ReleaseFault
	{
		get;
		set;
	}

	internal MemoryScanMaterializationStatus? CopyStatus
	{
		get;
		set;
	}

	/// <summary>Runs while the scan call is inside Cheat Engine, after its own cancellation check.</summary>
	internal Action? DuringStart
	{
		get;
		set;
	}

	/// <summary>Runs before the start's own cancellation check, as a token cancelled in a race would.</summary>
	internal Action? BeforeStartCheck
	{
		get;
		set;
	}

	/// <summary>Runs while the wait is inside Cheat Engine; Cheat Engine runs queued main-thread work there.</summary>
	internal Action? DuringWait
	{
		get;
		set;
	}

	internal string? HostErrorText
	{
		get;
		set;
	}

	internal bool HostErrorTextTruncated
	{
		get;
		set;
	}

	internal ValueScanReleaseStatuses ReleaseStatuses
	{
		get;
		set;
	} = new(TargetReleaseStatus.Released, TargetReleaseStatus.Released, MemoryScanTerminationStatus.NotRequired);

	internal int Destroys
	{
		get;
		private set;
	}

	internal FirstScanRequest? LastFirstScan
	{
		get;
		private set;
	}

	internal NextScanRequest? LastNextScan
	{
		get;
		private set;
	}

	public MemoryScanState State
	{
		get;
		set;
	} = MemoryScanState.New;

	public MemoryScanInvalidationReason InvalidationReason
	{
		get;
		set;
	}

	public MemoryScanCancellationMilestone LastCancellationMilestone
	{
		get;
		private set;
	}

	public ulong ReadResultCount()
	{
		Calls.Add("ResultCount");
		RequireState(MemoryScanState.ResultsReady);
		Begin();
		try
		{
			EnsureContext();
			if (CountFault is { } fault)
			{
				throw fault;
			}

			return ReportedCount ?? (ulong) Results.Count;
		}
		finally
		{
			End();
		}
	}

	public void StartFirstScan(in FirstScanRequest request, CancellationToken cancellationToken)
	{
		Calls.Add("StartFirstScan");
		Start(MemoryScanState.New, cancellationToken);
		LastFirstScan = request;
	}

	public void StartNextScan(in NextScanRequest request, CancellationToken cancellationToken)
	{
		Calls.Add("StartNextScan");
		Start(MemoryScanState.ResultsReady, cancellationToken);
		LastNextScan = request;
	}

	public void WaitForCompletion(CancellationToken cancellationToken)
	{
		Calls.Add("Wait");
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireState(MemoryScanState.Scanning);
		Begin();
		try
		{
			EnsureContext();
			ThrowIfCancelledBeforeNativeCall(cancellationToken);
			DuringWait?.Invoke();
			if (!_releaseDeferred)
			{
				if (WaitFault is { } fault)
				{
					Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
					throw fault;
				}

				Complete(MemoryScanState.ResultsReady);
			}

			ObserveCancellation(cancellationToken);
		}
		finally
		{
			End();
		}

		ThrowIfReleasedDuringCall();
	}

	public void Reset(CancellationToken cancellationToken)
	{
		Calls.Add("Reset");
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		ThrowIfDisposed();
		if (State == MemoryScanState.New)
		{
			return;
		}

		if (State == MemoryScanState.Scanning)
		{
			throw ScanFaults.State();
		}

		Begin();
		try
		{
			EnsureContext();
			ThrowIfCancelledBeforeNativeCall(cancellationToken);
			Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
			if (ResetFault is { } fault)
			{
				throw fault;
			}

			Complete(MemoryScanState.New);
			ObserveCancellation(cancellationToken);
		}
		finally
		{
			End();
		}
	}

	public MemoryScanMaterializationStatus TryCopyResultsPage(int firstResultIndex,
		Span<MemoryScanResult> destination, out ulong totalCount, out int written, CancellationToken cancellationToken)
	{
		Calls.Add("CopyPage");
		totalCount = 0;
		written = 0;
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireState(MemoryScanState.ResultsReady);
		Begin();
		try
		{
			if (CopyStatus is { } scripted)
			{
				return scripted;
			}

			if (cancellationToken.IsCancellationRequested)
			{
				LastCancellationMilestone = MemoryScanCancellationMilestone.CancelledBeforeNativeCall;
				return MemoryScanMaterializationStatus.Cancelled;
			}

			totalCount = ReportedCount ?? (ulong) Results.Count;
			if (totalCount == 0)
			{
				return MemoryScanMaterializationStatus.NoResults;
			}

			if ((ulong) firstResultIndex >= totalCount)
			{
				return MemoryScanMaterializationStatus.PageStartOutOfRange;
			}

			int length = (int) Math.Min((ulong) destination.Length, totalCount - (ulong) firstResultIndex);
			for (int index = 0; index < length; index++)
			{
				destination[index] = Results[firstResultIndex + index];
			}

			written = length;
			return MemoryScanMaterializationStatus.Success;
		}
		finally
		{
			End();
		}
	}

	public bool TryGetHostErrorText([NotNullWhen(true)] out string? text, out bool truncated)
	{
		ThrowIfDisposed();
		text = HostErrorText;
		truncated = HostErrorTextTruncated;
		return text is not null;
	}

	public ValueScanReleaseStatuses Release()
	{
		if (ReleaseFault is { } fault)
		{
			throw fault;
		}

		if (State == MemoryScanState.Disposed)
		{
			return _finalRelease ?? default;
		}

		if (_active)
		{
			// Called from inside one of this session's Cheat Engine calls: deferred until that call returns.
			_releaseDeferred = true;
			return default;
		}

		CompleteRelease();
		return _finalRelease ?? default;
	}

	private void Start(MemoryScanState required, CancellationToken cancellationToken)
	{
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireState(required);
		Begin();
		try
		{
			EnsureContext();
			BeforeStartCheck?.Invoke();
			ThrowIfCancelledBeforeNativeCall(cancellationToken);
			Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
			DuringStart?.Invoke();
			if (StartFault is { } fault)
			{
				throw fault;
			}

			Complete(MemoryScanState.Scanning);
			ObserveCancellation(cancellationToken);
		}
		finally
		{
			End();
		}

		ThrowIfReleasedDuringCall();
	}

	private void EnsureContext()
	{
		if (ContextFault is not { } fault)
		{
			return;
		}

		Invalidate(fault.FailureKind == MemoryScanFailureKind.RuntimeInvalidated
			? MemoryScanInvalidationReason.RuntimeIdentityChanged
			: MemoryScanInvalidationReason.TargetChanged);
		throw fault;
	}

	private void RequireState(MemoryScanState expected)
	{
		ThrowIfDisposed();
		if (_active || State != expected)
		{
			throw ScanFaults.State();
		}
	}

	private void Begin()
	{
		if (_active)
		{
			throw ScanFaults.State();
		}

		_active = true;
	}

	private void End()
	{
		_active = false;
		if (!_releaseDeferred)
		{
			return;
		}

		_releaseDeferred = false;
		CompleteRelease();
	}

	private void CompleteRelease()
	{
		Destroys++;
		State = MemoryScanState.Disposed;
		_finalRelease = ReleaseStatuses;
	}

	private void ThrowIfDisposed()
	{
		if (State == MemoryScanState.Disposed)
		{
			throw new ObjectDisposedException("MemoryScanSession", "The memory-scan session was disposed.");
		}
	}

	private void ThrowIfReleasedDuringCall()
	{
		if (State == MemoryScanState.Disposed)
		{
			throw new ObjectDisposedException("MemoryScanSession",
				"The memory-scan session was released by a call made while it was inside a Cheat Engine call.");
		}
	}

	private void ThrowIfCancelledBeforeNativeCall(CancellationToken cancellationToken)
	{
		if (!cancellationToken.IsCancellationRequested)
		{
			return;
		}

		LastCancellationMilestone = MemoryScanCancellationMilestone.CancelledBeforeNativeCall;
		throw new OperationCanceledException(cancellationToken);
	}

	private void ObserveCancellation(CancellationToken cancellationToken)
	{
		LastCancellationMilestone = cancellationToken.IsCancellationRequested
			? MemoryScanCancellationMilestone.ObservedAfterNativeCall
			: MemoryScanCancellationMilestone.None;
	}

	private void Invalidate(MemoryScanInvalidationReason reason)
	{
		State = MemoryScanState.Invalidated;
		InvalidationReason = reason;
	}

	private void Complete(MemoryScanState state)
	{
		State = state;
		InvalidationReason = MemoryScanInvalidationReason.None;
	}
}

/// <summary>Builds the memory-scan exceptions that CheatEngine.SDK constructs internally only.</summary>
internal static class ScanFaults
{
	/// <summary>A state refusal; the Client classifies it by its type alone.</summary>
	internal static MemoryScanStateException State()
	{
		return (MemoryScanStateException) RuntimeHelpers.GetUninitializedObject(typeof(MemoryScanStateException));
	}

	/// <summary>A memory-scan failure of the given category.</summary>
	internal static MemoryScanException Scan(MemoryScanFailureKind kind)
	{
		MemoryScanException exception =
			(MemoryScanException) RuntimeHelpers.GetUninitializedObject(typeof(MemoryScanException));
		FieldInfo field = typeof(MemoryScanException).GetField("<FailureKind>k__BackingField",
							  BindingFlags.Instance | BindingFlags.NonPublic)
						  ?? throw new InvalidOperationException(
							  "MemoryScanException.FailureKind is no longer an auto-property.");
		field.SetValue(exception, kind);
		return exception;
	}
}
