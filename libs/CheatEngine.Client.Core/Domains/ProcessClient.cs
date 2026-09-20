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

		ProcessSnapshot captured = default;
		if (!_dispatcher.TryInvoke(
			    () =>
			    {
				    _host.OpenProcess(processId.Value);
				    captured = CaptureCurrent("Processes.Attach");
				    if (captured.Id != processId)
				    {
					    throw new InvalidOperationException("Cheat Engine did not select the requested process.");
				    }
			    },
			    out failure,
			    cancellationToken))
		{
			snapshot = default;
			return false;
		}

		snapshot = captured;
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

	private bool TryReadCurrent(
		string operation,
		out ProcessSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		ProcessSnapshot captured = default;
		if (!_dispatcher.TryInvoke(() => captured = CaptureCurrent(operation), out failure, cancellationToken))
		{
			snapshot = default;
			if (failure.Kind == CheatEngineFailureKind.OperationRejected)
			{
				failure = new CheatEngineFailure(
					CheatEngineFailureKind.TargetNotAttached,
					operation,
					failure.Message,
					failure.Exception);
			}

			return false;
		}

		snapshot = captured;
		return true;
	}

	private ProcessSnapshot CaptureCurrent(string operation)
	{
		long processId = _host.GetOpenedProcessId();
		if (processId is <= 0 or > int.MaxValue)
		{
			ClearObservedSelection(operation);
			throw new InvalidOperationException("Cheat Engine has no selected local target process.");
		}

		TargetProcessId id = new(checked((int) processId));
		if (!_host.TryGetLocalProcess(id.Value, out LocalProcessInfo process))
		{
			ClearObservedSelection(operation);
			throw new InvalidOperationException("The selected process no longer exists locally.");
		}

		if (process.Id != id.Value)
		{
			throw new EngineMarshallingException(
				"Processes.GetCurrent",
				EngineMarshallingDirection.Result,
				"metadata for the selected process identifier",
				$"metadata for process {process.Id}");
		}

		CheatEngineArchitecture architecture = TryGetTargetArchitecture();
		return ObserveSelection(id, process, architecture, operation);
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

	private readonly record struct ProcessSelection(TargetProcessId Id, CheatEngineArchitecture Architecture);
}
