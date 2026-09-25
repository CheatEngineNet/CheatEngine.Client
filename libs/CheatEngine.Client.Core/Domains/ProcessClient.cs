using System.ComponentModel;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Owns deterministic target selection without taking ownership of Cheat Engine's global process state.</summary>
/// <remarks>
///     <para>
///         Every observation goes through <see cref="IRuntimeObservationPort" />; the one call that changes Cheat Engine's
///         selection is <see cref="IProcessSelectionPort.SelectAndObserve" />, reached only from <see cref="TryAttach" />.
///     </para>
///     <para>
///         The selection identity is the PID and, for a local process, its incarnation: the PID and creation time that
///         CheatEngine.SDK's <c>TargetSelection</c> observed. A known incarnation is checked with
///         <c>TargetSelection.ValidateCurrent</c>, so the selection epoch advances when the same PID denotes another
///         process. Like the SDK, the Client cannot see a selection that changed and changed back between two
///         observations (A-B-A); only a later observation of another PID or incarnation advances the epoch. A CEServer or
///         unknown backend has no local incarnation and no local metadata.
///     </para>
/// </remarks>
internal sealed class ProcessClient : IProcessClient, ITargetSelectionBinder
{
	private readonly Action<string>? _admitStatefulOperation;
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly IProcessHost _host;
	private readonly CoreLifetime? _lifetime;
	private readonly IRuntimeObservationPort _observations;
	private readonly IProcessSelectionPort _selection;
	private readonly Lock _selectionGate = new();
	private readonly TargetSelectionLifetime _selectionLifetime;
	private ProcessSelection? _lastSelection;

	internal ProcessClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime)
		: this(dispatcher, new LocalProcessHost(), SdkRuntimeObservationPort.Instance, SdkProcessSelectionPort.Instance,
			lifetime)
	{
	}

	internal ProcessClient(ICheatEngineDispatcher dispatcher, IProcessHost host,
		IRuntimeObservationPort observations, IProcessSelectionPort selection, CoreLifetime lifetime)
		: this(dispatcher, host, observations, selection,
			lifetime?.TargetSelection ?? throw new ArgumentNullException(nameof(lifetime)), lifetime.ThrowIfInactive,
			lifetime)
	{
	}

	internal ProcessClient(
		ICheatEngineDispatcher dispatcher,
		IProcessHost host,
		IRuntimeObservationPort observations,
		IProcessSelectionPort selection,
		TargetSelectionLifetime selectionLifetime)
		: this(dispatcher, host, observations, selection, selectionLifetime, null)
	{
	}

	internal ProcessClient(ICheatEngineDispatcher dispatcher, IProcessHost host, IRuntimeObservationPort observations,
		IProcessSelectionPort selection, TargetSelectionLifetime selectionLifetime,
		Action<string>? admitStatefulOperation, CoreLifetime? lifetime = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_host = host ?? throw new ArgumentNullException(nameof(host));
		_observations = observations ?? throw new ArgumentNullException(nameof(observations));
		_selection = selection ?? throw new ArgumentNullException(nameof(selection));
		_selectionLifetime = selectionLifetime ?? throw new ArgumentNullException(nameof(selectionLifetime));
		_admitStatefulOperation = admitStatefulOperation;
		_lifetime = lifetime;
	}

	public bool TryGetCurrentProcess(
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryReadCurrent("Processes.GetCurrentProcess", out snapshot, out failure, cancellationToken);
	}

	public ProcessSnapshot GetCurrentProcess(CancellationToken cancellationToken = default)
	{
		if (TryGetCurrentProcess(out ProcessSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
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
		bool invoked = _dispatcher.TryInvoke(() => captured = SelectAndCapture(), out failure, cancellationToken);
		ReportSelectionAdvance(captured.Advance, "Processes.Attach");
		if (!invoked)
		{
			snapshot = default;
			return false;
		}

		return TryGetCapturedSnapshot(captured, "Processes.Attach", out snapshot, out failure);

		// The only caller of SelectAndObserve (architecture ratchet): select, then observe Cheat Engine's selection again
		// so the selection epoch follows whatever it now selects, even when the selection is refused.
		CurrentProcessCapture SelectAndCapture()
		{
			ProcessOperationStatus status;
			try
			{
				// SelectAndObserve also resets Cheat Engine's configured pointer size (spike C3 D3(c)).
				status = _selection.SelectAndObserve(processId, out _);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				return new CurrentProcessCapture(exception);
			}

			CurrentProcessCapture observed = CaptureCurrent("Processes.Attach");
			if (!status.IsSuccess)
			{
				return new CurrentProcessCapture(CurrentProcessCaptureFailure.SelectionRefused, status)
				{
					Advance = observed.Advance
				};
			}

			// Cheat Engine confirmed the selection; a different PID now means that it changed again since.
			return observed.Failure == CurrentProcessCaptureFailure.None && observed.Snapshot.Id != processId
				? new CurrentProcessCapture(CurrentProcessCaptureFailure.Unobserved,
					ProcessOperationStatus.TargetChanged)
				{
					Advance = observed.Advance
				}
				: observed;
		}
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

	public bool TryGetLocalProcesses(
		LocalProcessEnumerationRequest request,
		out LocalProcessEnumerationResult result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		// An offline diagnostic of the local catalog: no activation admission and no Cheat Engine dispatch.
		return LocalProcessCatalog.TryEnumerate(_host, request, out result, out failure, cancellationToken);
	}

	public LocalProcessEnumerationResult GetLocalProcesses(
		LocalProcessEnumerationRequest request,
		CancellationToken cancellationToken = default)
	{
		if (TryGetLocalProcesses(request, out LocalProcessEnumerationResult result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	/// <inheritdoc />
	/// <remarks>
	///     An incarnation describes a local process. The ISA and width of the owner's target are not observed here, so the
	///     last values known for the same selection are kept, as for any observation that leaves a fact unknown.
	/// </remarks>
	public TargetSelectionBinding BindOwner(TargetProcessIncarnation incarnation, string operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ProcessSelection bound = new(new TargetProcessId(incarnation.ProcessId), TargetBackend.LocalProcess,
			CheatEngineArchitecture.Unknown, PointerSize.Unknown, incarnation);
		lock (_selectionGate)
		{
			_ = RecordSelection(bound, operation, out SelectionAdvance? advance);
			return new TargetSelectionBinding(_selectionLifetime.Epoch, advance?.Reason);
		}
	}

	/// <inheritdoc />
	public void ReportBinding(TargetSelectionBinding binding, string operation)
	{
		if (binding.AdvanceReason is { } reason)
		{
			ReportSelectionAdvance(new SelectionAdvance(binding.SelectionEpoch, reason), operation);
		}
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
	///     before and after the facts), then, for a local process only, its incarnation and optional local metadata. A
	///     status other than success is returned as a classified failure; an SDK fault of a Cheat Engine or local-catalog
	///     call is captured and returned as a failure.
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

		// A local creation time or catalog entry describes a local process only, never a CEServer target or a PID whose
		// backend is not established (audit A12-05).
		IncarnationRead incarnation = IncarnationRead.Unknown;
		LocalProcessInfo process = default;
		try
		{
			if (target.Backend == TargetBackend.LocalProcess)
			{
				incarnation = ReadIncarnation(id);
				if (incarnation.SelectionChanged)
				{
					return new CurrentProcessCapture(CurrentProcessCaptureFailure.Unobserved,
						ProcessOperationStatus.TargetChanged);
				}

				if (_host.TryGetLocalProcess(id.Value, out LocalProcessInfo local))
				{
					if (local.Id != id.Value)
					{
						return new CurrentProcessCapture(CurrentProcessCaptureFailure.InvalidLocalMetadata,
							target.Status, local.Id);
					}

					process = local;
				}
			}
		}
		catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
		{
			return new CurrentProcessCapture(exception);
		}

		ProcessSnapshot snapshot = ObserveSelection(
			new ProcessSelection(id, target.Backend, target.Architecture, target.Bitness, incarnation.Incarnation),
			process, target.ConfiguredPointerSizeBytes, operation, out SelectionAdvance? advance);
		return new CurrentProcessCapture(snapshot)
		{
			Advance = advance
		};
	}

	/// <summary>
	///     Reads the incarnation of the selected local process: the last known incarnation of the same PID is checked
	///     with <c>ValidateCurrent</c>, otherwise the selection is observed. A failed or unqualified observation leaves
	///     the incarnation unknown, which is no evidence of a change; another PID, no target, a file opened as a process
	///     or a CEServer backend means that the selection changed after the target facts were read.
	/// </summary>
	private IncarnationRead ReadIncarnation(TargetProcessId id)
	{
		TargetProcessIncarnation? known;
		lock (_selectionGate)
		{
			known = _lastSelection is { Incarnation: { } last } previous && previous.Id == id ? last : null;
		}

		if (known is { } expected)
		{
			TargetIdentityFacts check = _observations.ValidateSelection(expected);
			return RuntimeObservationMapping.ToIncarnationComparison(check.Kind) switch
			{
				IncarnationComparison.Current => IncarnationRead.Of(expected),
				IncarnationComparison.ProcessReused when check.Observed.Incarnation is { } reused =>
					IncarnationRead.Of(reused),
				IncarnationComparison.SelectionChanged => IncarnationRead.Changed,
				_ => IncarnationRead.Unknown
			};
		}

		TargetSelectionFacts observed = _observations.ObserveSelection();
		if (observed.SelectedProcessId is { } selected && selected != id.Value)
		{
			return IncarnationRead.Changed;
		}

		return RuntimeObservationMapping.ToSelectionIdentity(observed.Status) switch
		{
			SelectionIdentity.Qualified when observed.Incarnation is { } incarnation => IncarnationRead.Of(incarnation),
			SelectionIdentity.NoTarget or SelectionIdentity.FileAsProcess or SelectionIdentity.RemoteBackend =>
				IncarnationRead.Changed,
			_ => IncarnationRead.Unknown
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
			// A fault of the selection call, of an observation or of the local catalog: whether a selection change
			// happened is unknown, so the host effect stays unknown.
			CurrentProcessCaptureFailure.Faulted => SdkBoundary.Translate(operation, captured.Fault!,
				CheatEngineHostEffect.Unknown, _lifetime),
			// SelectAndObserve refused or could not confirm the selection: what Cheat Engine now selects is unknown.
			CurrentProcessCaptureFailure.SelectionRefused => RuntimeObservationMapping.ToFailure(operation,
				captured.Status, CheatEngineHostEffect.Unknown),
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
	///     Records the observed selection and advances the selection epoch when the PID changed, when the same PID denotes
	///     another incarnation, or when a known backend, ISA or process width changed to a different known value. An
	///     unknown fact neither advances the epoch nor erases the last fact known for the same selection, so a transient
	///     observation failure does not invalidate target-bound leases and does not weaken the identity to the PID alone.
	/// </summary>
	private ProcessSnapshot ObserveSelection(ProcessSelection observed, LocalProcessInfo process,
		int? configuredPointerSizeBytes, string operation, out SelectionAdvance? advance)
	{
		lock (_selectionGate)
		{
			ProcessSelection selection = RecordSelection(observed, operation, out advance);
			// The configured pointer size is a fact of this observation, not of the selection identity, so it is never
			// merged with an earlier value. Local metadata and the start time describe a local process only.
			bool isLocal = selection.Backend == TargetBackend.LocalProcess;
			return new ProcessSnapshot(
				selection.Id,
				isLocal ? process.Name : null,
				isLocal ? process.ExecutablePath : null,
				selection.Backend,
				selection.Architecture,
				selection.Width,
				configuredPointerSizeBytes,
				isLocal && selection.Incarnation is { } incarnation
					? new DateTimeOffset(incarnation.StartedAtUtcTicks, TimeSpan.Zero)
					: null,
				_selectionLifetime.Epoch);
		}
	}

	/// <summary>
	///     Records an observed selection under <see cref="_selectionGate" />, which the caller holds: the epoch advances when
	///     <see cref="GetChangeReason" /> finds a change, otherwise the observation is merged into the last one.
	/// </summary>
	private ProcessSelection RecordSelection(ProcessSelection observed, string operation,
		out SelectionAdvance? advance)
	{
		advance = null;
		ProcessSelection selection = observed;
		if (_lastSelection is { } previous)
		{
			if (GetChangeReason(previous, observed) is { } reason)
			{
				advance = new SelectionAdvance(_selectionLifetime.Advance(operation), reason);
			}
			else
			{
				selection = previous.Merge(observed);
			}
		}

		_lastSelection = selection;
		return selection;
	}

	private static string? GetChangeReason(ProcessSelection previous, ProcessSelection current)
	{
		if (previous.Id != current.Id)
		{
			return "PidChanged";
		}

		if (previous.Incarnation is { } before && current.Incarnation is { } after && before != after)
		{
			return "ProcessReused";
		}

		if (previous.Backend != TargetBackend.Unknown && current.Backend != TargetBackend.Unknown &&
			previous.Backend != current.Backend)
		{
			return "BackendChanged";
		}

		if (previous.Architecture != CheatEngineArchitecture.Unknown &&
			current.Architecture != CheatEngineArchitecture.Unknown && previous.Architecture != current.Architecture)
		{
			return "ArchitectureChanged";
		}

		return previous.Width.IsKnown && current.Width.IsKnown && previous.Width.Bytes != current.Width.Bytes
			? "WidthChanged"
			: null;
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

	/// <summary>The identity of one observed selection: PID, backend, ISA, process width and local incarnation.</summary>
	private readonly record struct ProcessSelection(
		TargetProcessId Id,
		TargetBackend Backend,
		CheatEngineArchitecture Architecture,
		PointerSize Width,
		TargetProcessIncarnation? Incarnation)
	{
		/// <summary>Keeps every fact of <paramref name="current" /> and the last known value of each unknown one.</summary>
		internal ProcessSelection Merge(ProcessSelection current)
		{
			return new ProcessSelection(current.Id,
				current.Backend == TargetBackend.Unknown ? Backend : current.Backend,
				current.Architecture == CheatEngineArchitecture.Unknown ? Architecture : current.Architecture,
				current.Width.IsKnown ? current.Width : Width,
				current.Incarnation ?? Incarnation);
		}
	}

	/// <summary>The incarnation read of one capture: a known incarnation, unknown, or a changed selection.</summary>
	private readonly record struct IncarnationRead(TargetProcessIncarnation? Incarnation, bool SelectionChanged)
	{
		internal static IncarnationRead Unknown => default;

		internal static IncarnationRead Changed => new(null, true);

		internal static IncarnationRead Of(TargetProcessIncarnation incarnation)
		{
			return new IncarnationRead(incarnation, false);
		}
	}

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
		SelectionRefused,
		Faulted
	}
}
