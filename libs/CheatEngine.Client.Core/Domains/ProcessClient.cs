using System.ComponentModel;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Owns deterministic target selection without taking ownership of Cheat Engine's global process state.</summary>
internal sealed class ProcessClient : IProcessClient
{
	private readonly Action<string>? _admitStatefulOperation;
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly IProcessHost _host;
	private readonly CoreLifetime? _lifetime;
	private readonly Lock _selectionGate = new();
	private readonly TargetSelectionLifetime _selectionLifetime;
	private ProcessSelection? _lastSelection;

	internal ProcessClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime)
		: this(dispatcher, new LocalProcessHost(), lifetime)
	{
	}

	internal ProcessClient(ICheatEngineDispatcher dispatcher, IProcessHost host, CoreLifetime lifetime)
		: this(dispatcher, host, lifetime?.TargetSelection ?? throw new ArgumentNullException(nameof(lifetime)),
			lifetime.ThrowIfInactive, lifetime)
	{
	}

	internal ProcessClient(
		ICheatEngineDispatcher dispatcher,
		IProcessHost host,
		TargetSelectionLifetime selectionLifetime)
		: this(dispatcher, host, selectionLifetime, null)
	{
	}

	internal ProcessClient(ICheatEngineDispatcher dispatcher, IProcessHost host,
		TargetSelectionLifetime selectionLifetime, Action<string>? admitStatefulOperation,
		CoreLifetime? lifetime = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_host = host ?? throw new ArgumentNullException(nameof(host));
		_selectionLifetime = selectionLifetime ?? throw new ArgumentNullException(nameof(selectionLifetime));
		_admitStatefulOperation = admitStatefulOperation;
		_lifetime = lifetime;
	}

	public bool TryGetCurrent(
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryReadCurrent("Processes.GetCurrent", out snapshot, out failure, cancellationToken);
	}

	public ProcessSnapshot GetCurrent(CancellationToken cancellationToken = default)
	{
		if (TryGetCurrent(out ProcessSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	public bool TryRefresh(
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryReadCurrent("Processes.Refresh", out snapshot, out failure, cancellationToken);
	}

	public ProcessSnapshot Refresh(CancellationToken cancellationToken = default)
	{
		if (TryRefresh(out ProcessSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	public bool TryAttach(
		TargetProcessId processId,
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (processId.Value <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(processId));
		}

		Admit("Processes.Attach");

		CurrentProcessCapture captured = default;
		bool invoked = _dispatcher.TryInvoke(
			() =>
			{
				try
				{
					// openProcess also resets Cheat Engine's configured pointer size (spike C3 D3(c)).
					_host.OpenProcess(processId.Value);
				}
				catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
				{
					captured = new CurrentProcessCapture(exception);
					return;
				}

				captured = CaptureCurrent("Processes.Attach");
			},
			out failure,
			cancellationToken);
		ReportSelectionAdvance(captured.Advance, "Processes.Attach");
		if (!invoked)
		{
			snapshot = default;
			return false;
		}

		if (!TryGetCapturedSnapshot(captured, "Processes.Attach", out snapshot, out failure))
		{
			return false;
		}

		if (snapshot.Id != processId)
		{
			snapshot = default;
			failure = new CheatEngineFailure(
				CheatEngineFailureKind.OperationRejected,
				"Processes.Attach",
				"Cheat Engine did not select the requested process.");
			return false;
		}

		return true;
	}

	public ProcessSnapshot Attach(TargetProcessId processId, CancellationToken cancellationToken = default)
	{
		if (TryAttach(processId, out ProcessSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	public bool TryAttachExactName(
		string processName,
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		string expectedName = NormalizeExactProcessName(processName);
		Admit("Processes.AttachExactName");
		if (cancellationToken.IsCancellationRequested)
		{
			snapshot = default;
			failure = Cancelled("Processes.AttachExactName");
			return false;
		}

		IReadOnlyList<LocalProcessInfo> matches;
		try
		{
			matches = _host.FindProcessesByExactName(expectedName);
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception
											  or PlatformNotSupportedException)
		{
			snapshot = default;
			failure = new CheatEngineFailure(
				CheatEngineFailureKind.OperationRejected,
				"Processes.AttachExactName",
				"The local process catalog could not be searched for an attach candidate.",
				exception);
			return false;
		}

		if (matches.Count == 0)
		{
			snapshot = default;
			failure = new CheatEngineFailure(
				CheatEngineFailureKind.NotFound,
				"Processes.AttachExactName",
				$"No local process named '{processName}' was found.");
			return false;
		}

		if (matches.Count != 1)
		{
			snapshot = default;
			failure = new CheatEngineFailure(
				CheatEngineFailureKind.AmbiguousMatch,
				"Processes.AttachExactName",
				$"Several local processes named '{processName}' were found.");
			return false;
		}

		return TryAttach(new TargetProcessId(matches[0].Id), out snapshot, out failure, cancellationToken);
	}

	public ProcessSnapshot AttachExactName(string processName, CancellationToken cancellationToken = default)
	{
		if (TryAttachExactName(processName, out ProcessSnapshot snapshot, out CheatEngineFailure failure,
				cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	public bool TryAttachForeground(
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryUnavailable("Processes.AttachForeground", out snapshot, out failure, cancellationToken);
	}

	public ProcessSnapshot AttachForeground(CancellationToken cancellationToken = default)
	{
		if (TryAttachForeground(out ProcessSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	public bool TryCreate(
		ProcessStartRequest request,
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ValidateStartRequest(request);
		return TryUnavailable("Processes.Create", out snapshot, out failure, cancellationToken);
	}

	public ProcessSnapshot Create(ProcessStartRequest request, CancellationToken cancellationToken = default)
	{
		if (TryCreate(request, out ProcessSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	public bool TryPause(
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryUnavailable("Processes.Pause", out snapshot, out failure, cancellationToken);
	}

	public ProcessSnapshot Pause(CancellationToken cancellationToken = default)
	{
		if (TryPause(out ProcessSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	public bool TryResumeExecution(
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryUnavailable("Processes.Resume", out snapshot, out failure, cancellationToken);
	}

	public ProcessSnapshot ResumeExecution(CancellationToken cancellationToken = default)
	{
		if (TryResumeExecution(out ProcessSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	public bool TryGetPauseState(
		out ProcessPauseSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryUnavailable("Processes.GetPauseState", out snapshot, out failure, cancellationToken);
	}

	public ProcessPauseSnapshot GetPauseState(CancellationToken cancellationToken = default)
	{
		if (TryGetPauseState(out ProcessPauseSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	private bool TryReadCurrent(
		string operation,
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		CurrentProcessCapture captured = default;
		bool invoked = _dispatcher.TryInvoke(() => captured = CaptureCurrent(operation), out failure,
			cancellationToken);
		ReportSelectionAdvance(captured.Advance, operation);
		if (!invoked)
		{
			snapshot = default;
			return false;
		}

		return TryGetCapturedSnapshot(captured, operation, out snapshot, out failure);
	}

	/// <summary>
	///     Reports a selection-epoch advance recorded by the dispatched capture, after the callback returned (EventId
	///     1100): the epochs and the reason only, never the process identifier or name.
	/// </summary>
	private void ReportSelectionAdvance(SelectionAdvance? advance, string operation)
	{
		if (advance is { } advanced && _lifetime is { } lifetime)
		{
			lifetime.Diagnostics.TargetSelectionAdvanced(lifetime.Epoch, advanced.SelectionEpoch, operation,
				advanced.Reason);
		}
	}

	/// <summary>
	///     Observes the selected target with the PID first and bracketed (spike C3 D2): the opened PID, the local
	///     metadata, the target facts, then the opened PID again. An SDK exception from a fact probe leaves that fact
	///     unknown; any other fault of a Cheat Engine or local-catalog call is captured and returned as a failure.
	/// </summary>
	private CurrentProcessCapture CaptureCurrent(string operation)
	{
		long processId;
		try
		{
			processId = _host.GetOpenedProcessId();
		}
		catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
		{
			return new CurrentProcessCapture(exception);
		}

		if (processId is <= 0 or > int.MaxValue)
		{
			return new CurrentProcessCapture(CurrentProcessCaptureFailure.NoTargetSelected)
			{
				Advance = ClearObservedSelection(operation)
			};
		}

		TargetProcessId id = new(checked((int) processId));
		bool hasLocalMetadata;
		LocalProcessInfo process;
		ObservedTargetArchitecture facts;
		try
		{
			hasLocalMetadata = _host.TryGetLocalProcess(id.Value, out process);
			if (hasLocalMetadata && process.Id != id.Value)
			{
				return new CurrentProcessCapture(CurrentProcessCaptureFailure.InvalidLocalMetadata, process.Id);
			}

			facts = TargetArchitectureObserver.ObserveSelected(_host, processId, readConfiguredPointerSize: false);
		}
		catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
		{
			return new CurrentProcessCapture(exception);
		}

		if (facts.TargetChangedDuringObservation)
		{
			return new CurrentProcessCapture(CurrentProcessCaptureFailure.TargetChanged);
		}

		ProcessSnapshot snapshot = ObserveSelection(id, hasLocalMetadata ? process : default, facts.Architecture,
			facts.ProcessPointerSize, operation, out SelectionAdvance? advance);
		return new CurrentProcessCapture(snapshot)
		{
			Advance = advance
		};
	}

	private bool TryGetCapturedSnapshot(
		CurrentProcessCapture captured,
		string operation,
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure)
	{
		if (captured.Failure == CurrentProcessCaptureFailure.None)
		{
			snapshot = captured.Snapshot;
			failure = default;
			return true;
		}

		snapshot = default;
		failure = captured.Failure switch
		{
			// A fault of openProcess, of the PID read or of the local catalog: whether a selection change happened is
			// unknown, so the host effect stays unknown.
			CurrentProcessCaptureFailure.Faulted => SdkBoundary.Translate(operation, captured.Fault!,
				CheatEngineHostEffect.Unknown, _lifetime),
			CurrentProcessCaptureFailure.TargetChanged => new CheatEngineFailure(
				CheatEngineFailureKind.IndeterminateHostResult,
				operation,
				"Cheat Engine's selected target changed while the Client observed it, so the observation cannot be " +
				"attributed to one target; read the current process again.",
				null,
				CheatEngineHostEffect.Completed),
			CurrentProcessCaptureFailure.NoTargetSelected => new CheatEngineFailure(
				CheatEngineFailureKind.TargetNotAttached,
				operation,
				"Cheat Engine has no selected local target process."),
			CurrentProcessCaptureFailure.InvalidLocalMetadata => new CheatEngineFailure(
				CheatEngineFailureKind.InvalidHostResult,
				operation,
				"The selected target's local process metadata did not match its identifier.",
				new EngineMarshallingException(
					operation,
					EngineMarshallingDirection.Result,
					"metadata for the selected process identifier",
					$"metadata for process {captured.ObservedProcessId}")),
			_ => throw new InvalidOperationException("The current process capture produced an unknown failure.")
		};
		return false;
	}

	/// <summary>
	///     Records the observed selection and advances the selection epoch when the PID changed, or when a known fact
	///     changed to a different known value. An unknown fact neither advances the epoch nor erases the last fact known
	///     for the same selection, so a transient probe failure does not invalidate target-bound leases and does not
	///     weaken the identity to the PID alone.
	/// </summary>
	private ProcessSnapshot ObserveSelection(
		TargetProcessId id,
		LocalProcessInfo process,
		CheatEngineArchitecture architecture,
		PointerSize width,
		string operation,
		out SelectionAdvance? advance)
	{
		advance = null;
		lock (_selectionGate)
		{
			ProcessSelection selection = new(id, architecture, width);
			if (_lastSelection is { } previous)
			{
				string? reason = previous.Id != id
					? "PidChanged"
					: IsKnownChange(previous.Architecture, architecture)
						? "ArchitectureChanged"
						: IsKnownChange(previous.Width, width)
							? "WidthChanged"
							: null;
				if (reason is not null)
				{
					advance = new SelectionAdvance(_selectionLifetime.Advance(operation), reason);
				}
				else
				{
					selection = new ProcessSelection(id,
						architecture == CheatEngineArchitecture.Unknown ? previous.Architecture : architecture,
						width.IsKnown ? width : previous.Width);
				}
			}

			_lastSelection = selection;
			return new ProcessSnapshot(id, process.Name, process.ExecutablePath, selection.Architecture,
				selection.Width, _selectionLifetime.Epoch);
		}
	}

	private static bool IsKnownChange(CheatEngineArchitecture previous, CheatEngineArchitecture current)
	{
		return previous != CheatEngineArchitecture.Unknown && current != CheatEngineArchitecture.Unknown &&
			   previous != current;
	}

	private static bool IsKnownChange(PointerSize previous, PointerSize current)
	{
		return previous.IsKnown && current.IsKnown && previous.Bytes != current.Bytes;
	}

	private SelectionAdvance? ClearObservedSelection(string operation)
	{
		lock (_selectionGate)
		{
			if (_lastSelection is null)
			{
				return null;
			}

			_lastSelection = null;
			return new SelectionAdvance(_selectionLifetime.Advance(operation), "TargetDetached");
		}
	}

	private static string NormalizeExactProcessName(string processName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(processName);
		if (processName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
		{
			throw new ArgumentException("An exact process name must not include a directory path.",
				nameof(processName));
		}

		string normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
			? processName[..^4]
			: processName;
		if (normalized.Length == 0)
		{
			throw new ArgumentException("An exact process name must include a file name before '.exe'.",
				nameof(processName));
		}

		return normalized;
	}

	private static void ValidateStartRequest(ProcessStartRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.ExecutablePath))
		{
			throw new ArgumentException("The executable path must not be empty.", nameof(request));
		}

		if (!Path.IsPathFullyQualified(request.ExecutablePath))
		{
			throw new ArgumentException("The executable path must be absolute.", nameof(request));
		}

		if (request.WorkingDirectory is { } directory && !Path.IsPathFullyQualified(directory))
		{
			throw new ArgumentException("The working directory must be absolute when specified.", nameof(request));
		}
	}

	private bool TryUnavailable<T>(
		string operation,
		out T result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		Admit(operation);
		result = default!;
		failure = cancellationToken.IsCancellationRequested
			? Cancelled(operation)
			: new CheatEngineFailure(
				CheatEngineFailureKind.CapabilityUnavailable,
				operation,
				"This operation requires a validated Cheat Engine process-control binding.");
		return false;
	}

	private void Admit(string operation)
	{
		_admitStatefulOperation?.Invoke(operation);
	}

	private static CheatEngineFailure Cancelled(string operation)
	{
		return new CheatEngineFailure(
			CheatEngineFailureKind.Cancelled,
			operation,
			"The operation was cancelled before process-host admission.");
	}

	/// <summary>The identity of one observed selection: PID, ISA and process width.</summary>
	private readonly record struct ProcessSelection(
		TargetProcessId Id,
		CheatEngineArchitecture Architecture,
		PointerSize Width);

	/// <summary>A selection-epoch advance made by a capture: the new epoch and the closed reason name.</summary>
	private readonly record struct SelectionAdvance(long SelectionEpoch, string Reason);

	private readonly record struct CurrentProcessCapture(
		ProcessSnapshot Snapshot,
		CurrentProcessCaptureFailure Failure,
		int ObservedProcessId,
		Exception? Fault)
	{
		/// <summary>Gets the selection-epoch advance this capture made, reported after the dispatched callback returned.</summary>
		internal SelectionAdvance? Advance
		{
			get;
			init;
		}

		internal CurrentProcessCapture(ProcessSnapshot snapshot)
			: this(snapshot, CurrentProcessCaptureFailure.None, 0, null)
		{
		}

		internal CurrentProcessCapture(CurrentProcessCaptureFailure failure, int observedProcessId = 0)
			: this(default, failure, observedProcessId, null)
		{
		}

		internal CurrentProcessCapture(Exception fault)
			: this(default, CurrentProcessCaptureFailure.Faulted, 0, fault)
		{
		}
	}

	private enum CurrentProcessCaptureFailure
	{
		None,
		NoTargetSelected,
		InvalidLocalMetadata,
		TargetChanged,
		Faulted
	}
}
