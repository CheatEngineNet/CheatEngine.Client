using System.Collections.Immutable;
using System.ComponentModel;

using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Provides the explicitly local, Cheat-Engine-independent process diagnostic contract.</summary>
internal sealed class LocalProcessDiagnostics(IProcessHost host) : ILocalProcessDiagnostics
{
	private readonly IProcessHost _host = host ?? throw new ArgumentNullException(nameof(host));

	public bool TryGetProcesses(
		ProcessEnumerationRequest request,
		out ProcessEnumerationResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.MaximumItems);
		if (request.NameContains is { Length: 0 })
		{
			throw new ArgumentException("A process-name filter must be null or non-empty.", nameof(request));
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return Cancel(out result, out failure);
		}

		try
		{
			IReadOnlyList<LocalProcessInfo> processes = _host.GetLocalProcesses();
			List<LocalProcessInfo> matches = new(processes.Count);
			foreach (LocalProcessInfo process in processes)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return Cancel(out result, out failure);
				}

				if (Matches(request, process))
				{
					matches.Add(process);
				}
			}

			matches.Sort(static (left, right) => left.Id.CompareTo(right.Id));
			int count = Math.Min(matches.Count, request.MaximumItems);
			ProcessInfoSnapshot[] snapshots = new ProcessInfoSnapshot[count];
			for (int index = 0; index < count; index++)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return Cancel(out result, out failure);
				}

				LocalProcessInfo process = matches[index];
				snapshots[index] = new ProcessInfoSnapshot(
					new LocalProcessId(process.Id),
					process.Name,
					process.ExecutablePath);
			}

			result = new ProcessEnumerationResult(ImmutableArray.Create(snapshots), matches.Count > count);
			failure = default;
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception
		                                  or PlatformNotSupportedException)
		{
			result = default;
			failure = new CheatEngineFailure(
				CheatEngineFailureKind.OperationRejected,
				"LocalProcesses.GetProcesses",
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

	private static bool Matches(ProcessEnumerationRequest request, LocalProcessInfo process)
	{
		return request.NameContains is null ||
		       process.Name?.Contains(request.NameContains, StringComparison.OrdinalIgnoreCase) == true;
	}

	private static bool Cancel(out ProcessEnumerationResult result, out CheatEngineFailure failure)
	{
		result = default;
		failure = new CheatEngineFailure(
			CheatEngineFailureKind.Cancelled,
			"LocalProcesses.GetProcesses",
			"The operation was cancelled before local-process materialization completed.");
		return false;
	}
}
