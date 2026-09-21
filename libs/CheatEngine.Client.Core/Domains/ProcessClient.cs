using System.Collections.Immutable;
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
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly IProcessHost _host;
	private readonly Lock _selectionGate = new();
	private readonly TargetSelectionLifetime _selectionLifetime;
	private ProcessSelection? _lastSelection;

	internal ProcessClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime)
		: this(dispatcher, new LocalProcessHost(),
			lifetime?.TargetSelection ?? throw new ArgumentNullException(nameof(lifetime)))
	{
	}

	internal ProcessClient(
		ICheatEngineDispatcher dispatcher,
		IProcessHost host,
		TargetSelectionLifetime selectionLifetime)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_host = host ?? throw new ArgumentNullException(nameof(host));
		_selectionLifetime = selectionLifetime ?? throw new ArgumentNullException(nameof(selectionLifetime));
	}

	public bool TryGetCurrent(
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryReadCurrent("Processes.GetCurrent", out snapshot, out failure, cancellationToken);
	}

	public bool TryGetProcesses(
		ProcessEnumerationRequest request,
		out ProcessEnumerationResult result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ValidateEnumerationRequest(request);
		if (cancellationToken.IsCancellationRequested)
		{
			result = default;
			failure = Cancelled("Processes.GetProcesses");
			return false;
		}

		try
		{
			IReadOnlyList<LocalProcessInfo> localProcesses = _host.GetLocalProcesses();
			List<LocalProcessInfo> matching = new(localProcesses.Count);
			for (int index = 0; index < localProcesses.Count; index++)
			{
				LocalProcessInfo process = localProcesses[index];
				if (Matches(request, process))
				{
					matching.Add(process);
				}
			}

			matching.Sort(static (left, right) => left.Id.CompareTo(right.Id));
			int materializedCount = Math.Min(matching.Count, request.MaximumItems);
			ProcessInfoSnapshot[] snapshots = new ProcessInfoSnapshot[materializedCount];
			for (int index = 0; index < materializedCount; index++)
			{
				LocalProcessInfo process = matching[index];
				snapshots[index] = new ProcessInfoSnapshot(
					new TargetProcessId(process.Id),
					process.Name,
					process.ExecutablePath);
			}

			result = new ProcessEnumerationResult(
				ImmutableArray.Create(snapshots),
				matching.Count > materializedCount);
			failure = default;
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
			                                  or Win32Exception or PlatformNotSupportedException)
		{
			result = default;
			failure = new CheatEngineFailure(
				CheatEngineFailureKind.OperationRejected,
				"Processes.GetProcesses",
				"The local process list could not be materialized.",
				exception);
			return false;
		}
	}

	public ProcessEnumerationResult GetProcesses(
		ProcessEnumerationRequest request,
		CancellationToken cancellationToken = default)
	{
		if (TryGetProcesses(request, out ProcessEnumerationResult result, out CheatEngineFailure failure,
			    cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
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

		CurrentProcessCapture captured = default;
		if (!_dispatcher.TryInvoke(
		    () =>
		    {
			    _host.OpenProcess(processId.Value);
			    captured = CaptureCurrent("Processes.Attach");
		    },
		    out failure,
		    cancellationToken))
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
		IReadOnlyList<LocalProcessInfo> matches = _host.FindProcessesByExactName(expectedName);
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
		if (!_dispatcher.TryInvoke(() => captured = CaptureCurrent(operation), out failure, cancellationToken))
		{
			snapshot = default;
			return false;
		}

		return TryGetCapturedSnapshot(captured, operation, out snapshot, out failure);
	}

	private CurrentProcessCapture CaptureCurrent(string operation)
	{
		long processId = _host.GetOpenedProcessId();
		if (processId is <= 0 or > int.MaxValue)
		{
			ClearObservedSelection(operation);
			return new CurrentProcessCapture(CurrentProcessCaptureFailure.NoTargetSelected);
		}

		TargetProcessId id = new(checked((int) processId));
		if (!_host.TryGetLocalProcess(id.Value, out LocalProcessInfo process))
		{
			ClearObservedSelection(operation);
			return new CurrentProcessCapture(CurrentProcessCaptureFailure.LocalProcessUnavailable);
		}

		if (process.Id != id.Value)
		{
			return new CurrentProcessCapture(CurrentProcessCaptureFailure.InvalidLocalMetadata, process.Id);
		}

		CheatEngineArchitecture architecture = TryGetTargetArchitecture();
		return new CurrentProcessCapture(ObserveSelection(id, process, architecture, operation));
	}

	private static bool TryGetCapturedSnapshot(
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
			CurrentProcessCaptureFailure.NoTargetSelected => new CheatEngineFailure(
				CheatEngineFailureKind.TargetNotAttached,
				operation,
				"Cheat Engine has no selected local target process."),
			CurrentProcessCaptureFailure.LocalProcessUnavailable => new CheatEngineFailure(
				CheatEngineFailureKind.TargetNotAttached,
				operation,
				"The selected target process is no longer available in local process metadata."),
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

	private CheatEngineArchitecture TryGetTargetArchitecture()
	{
		try
		{
			return _host.GetTargetArchitecture();
		}
		catch (EngineGlobalUnavailableException)
		{
			return CheatEngineArchitecture.Unknown;
		}
		catch (EngineCapabilityUnavailableException)
		{
			return CheatEngineArchitecture.Unknown;
		}
	}

	private ProcessSnapshot ObserveSelection(
		TargetProcessId id,
		LocalProcessInfo process,
		CheatEngineArchitecture architecture,
		string operation)
	{
		lock (_selectionGate)
		{
			ProcessSelection selection = new(id, architecture);
			if (_lastSelection is { } previous && previous != selection)
			{
				_selectionLifetime.Advance(operation);
			}

			_lastSelection = selection;
			return new ProcessSnapshot(
				id,
				process.Name,
				process.ExecutablePath,
				architecture,
				_selectionLifetime.Epoch);
		}
	}

	private void ClearObservedSelection(string operation)
	{
		lock (_selectionGate)
		{
			if (_lastSelection is null)
			{
				return;
			}

			_lastSelection = null;
			_selectionLifetime.Advance(operation);
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

	private static void ValidateEnumerationRequest(ProcessEnumerationRequest request)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.MaximumItems);
		if (request.NameContains is { Length: 0 })
		{
			throw new ArgumentException("A process-name filter must be null or non-empty.", nameof(request));
		}
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

	private static bool Matches(ProcessEnumerationRequest request, LocalProcessInfo process)
	{
		return request.NameContains is null ||
		       process.Name?.IndexOf(request.NameContains, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool TryUnavailable<T>(
		string operation,
		out T result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		result = default!;
		failure = cancellationToken.IsCancellationRequested
			? Cancelled(operation)
			: new CheatEngineFailure(
				CheatEngineFailureKind.CapabilityUnavailable,
				operation,
				"This operation requires a validated Cheat Engine process-control binding.");
		return false;
	}

	private static CheatEngineFailure Cancelled(string operation)
	{
		return new CheatEngineFailure(
			CheatEngineFailureKind.Cancelled,
			operation,
			"The operation was cancelled before process-host admission.");
	}

	private readonly record struct ProcessSelection(TargetProcessId Id, CheatEngineArchitecture Architecture);

	private readonly record struct CurrentProcessCapture(
		ProcessSnapshot Snapshot,
		CurrentProcessCaptureFailure Failure,
		int ObservedProcessId)
	{
		internal CurrentProcessCapture(ProcessSnapshot snapshot)
			: this(snapshot, CurrentProcessCaptureFailure.None, 0)
		{
		}

		internal CurrentProcessCapture(CurrentProcessCaptureFailure failure, int observedProcessId = 0)
			: this(default, failure, observedProcessId)
		{
		}
	}

	private enum CurrentProcessCaptureFailure
	{
		None,
		NoTargetSelected,
		LocalProcessUnavailable,
		InvalidLocalMetadata
	}
}
