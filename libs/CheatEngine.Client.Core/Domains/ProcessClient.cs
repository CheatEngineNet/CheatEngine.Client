using System.ComponentModel;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Owns deterministic target selection without taking ownership of Cheat Engine's global process state.</summary>
internal sealed class ProcessClient : IProcessClient
{
	private readonly Action<string>? _admitStatefulOperation;
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly IProcessHost _host;
	private readonly CoreLifetime? _lifetime;
	private readonly IRuntimeObservationPort _observations;
	private readonly Lock _selectionGate = new();
	private readonly TargetSelectionLifetime _selectionLifetime;
	private ProcessSelection? _lastSelection;

	internal ProcessClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime)
		: this(dispatcher, new LocalProcessHost(), SdkRuntimeObservationPort.Instance, lifetime)
	{
	}

	internal ProcessClient(ICheatEngineDispatcher dispatcher, IProcessHost host,
		IRuntimeObservationPort observations, CoreLifetime lifetime)
		: this(dispatcher, host, observations,
			lifetime?.TargetSelection ?? throw new ArgumentNullException(nameof(lifetime)), lifetime.ThrowIfInactive,
			lifetime)
	{
	}

	internal ProcessClient(
		ICheatEngineDispatcher dispatcher,
		IProcessHost host,
		IRuntimeObservationPort observations,
		TargetSelectionLifetime selectionLifetime)
		: this(dispatcher, host, observations, selectionLifetime, null)
	{
	}

	internal ProcessClient(ICheatEngineDispatcher dispatcher, IProcessHost host, IRuntimeObservationPort observations,
		TargetSelectionLifetime selectionLifetime, Action<string>? admitStatefulOperation,
		CoreLifetime? lifetime = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_host = host ?? throw new ArgumentNullException(nameof(host));
		_observations = observations ?? throw new ArgumentNullException(nameof(observations));
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

		failure.Throw(cancellationToken);
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

		failure.Throw(cancellationToken);
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
				"Cheat Engine did not select the requested process.",
				null,
				CheatEngineHostEffect.Unknown);
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

		failure.Throw(cancellationToken);
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
			failure = CancellationMapping.BeforeNativeCall("Processes.AttachExactName",
				"The operation was cancelled before process-host admission.");
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

		failure.Throw(cancellationToken);
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
	///     Observes the selected target through <see cref="TargetArchitectureObserver" /> (CheatEngine.SDK reads the PID
	///     before and after the facts), then reads optional local metadata. A status other than success is returned as a
	///     classified failure; an SDK fault of a Cheat Engine or local-catalog call is captured and returned as a failure.
	/// </summary>
	private CurrentProcessCapture CaptureCurrent(string operation)
	{
		ObservedTarget target;
		try
		{
			target = TargetArchitectureObserver.Observe(_observations);
		}
		catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
		{
			return new CurrentProcessCapture(exception);
		}

		if (target.NoTargetSelected || target.Backend == TargetBackend.FileAsProcess)
		{
			// No process, or a file opened as a process, is selected: an earlier process selection no longer holds.
			return new CurrentProcessCapture(
				target.NoTargetSelected
					? CurrentProcessCaptureFailure.NoTargetSelected
					: CurrentProcessCaptureFailure.Unobserved, target.Status)
			{
				Advance = ClearObservedSelection(operation)
			};
		}

		if (target.ProcessId is not { } id)
		{
			return new CurrentProcessCapture(CurrentProcessCaptureFailure.Unobserved, target.Status);
		}

		bool hasLocalMetadata;
		LocalProcessInfo process;
		try
		{
			hasLocalMetadata = _host.TryGetLocalProcess(id.Value, out process);
			if (hasLocalMetadata && process.Id != id.Value)
			{
				return new CurrentProcessCapture(CurrentProcessCaptureFailure.InvalidLocalMetadata, target.Status,
					process.Id);
			}
		}
		catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
		{
			return new CurrentProcessCapture(exception);
		}

		ProcessSnapshot snapshot = ObserveSelection(id, hasLocalMetadata ? process : default, target.Architecture,
			target.Bitness, operation, out SelectionAdvance? advance);
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
			// The SDK operation returned a factual status: its call completed without establishing target facts.
			CurrentProcessCaptureFailure.Unobserved => RuntimeObservationMapping.ToFailure(operation, captured.Status,
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

	private void Admit(string operation)
	{
		_admitStatefulOperation?.Invoke(operation);
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
		ProcessOperationStatus Status,
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
			: this(snapshot, CurrentProcessCaptureFailure.None, ProcessOperationStatus.Success, 0, null)
		{
		}

		internal CurrentProcessCapture(CurrentProcessCaptureFailure failure, ProcessOperationStatus status,
			int observedProcessId = 0)
			: this(default, failure, status, observedProcessId, null)
		{
		}

		internal CurrentProcessCapture(Exception fault)
			: this(default, CurrentProcessCaptureFailure.Faulted, default, 0, fault)
		{
		}
	}

	private enum CurrentProcessCaptureFailure
	{
		None,
		NoTargetSelected,
		InvalidLocalMetadata,
		Unobserved,
		Faulted
	}
}
