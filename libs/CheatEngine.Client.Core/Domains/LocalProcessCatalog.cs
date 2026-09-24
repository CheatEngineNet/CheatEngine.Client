using System.Collections.Immutable;
using System.ComponentModel;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Enumerates the local operating-system process catalog for <see cref="IProcessClient.TryGetLocalProcesses" />:
///     an offline diagnostic that never dispatches to Cheat Engine and needs no current activation.
/// </summary>
internal static class LocalProcessCatalog
{
	private const string Operation = "Processes.GetLocalProcesses";

	/// <summary>Copies the local processes that match <paramref name="request" />, ordered by identifier and bounded.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The request's bound is not positive.</exception>
	/// <exception cref="ArgumentException">The request's name filter is empty.</exception>
	internal static bool TryEnumerate(
		IProcessHost host,
		ProcessEnumerationRequest request,
		out ProcessEnumerationResult result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(host);
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
			IReadOnlyList<LocalProcessInfo> processes = host.GetLocalProcesses();
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
				Operation,
				"The local process list could not be materialized.",
				exception,
				CheatEngineHostEffect.NotStarted);
			return false;
		}
	}

	private static bool Matches(ProcessEnumerationRequest request, LocalProcessInfo process)
	{
		return request.NameContains is null ||
			   process.Name?.Contains(request.NameContains, StringComparison.OrdinalIgnoreCase) == true;
	}

	/// <summary>No Cheat Engine work is ever dispatched here, so a cancellation always reports NotStarted.</summary>
	private static bool Cancel(out ProcessEnumerationResult result, out CheatEngineFailure failure)
	{
		result = default;
		failure = CancellationMapping.BeforeNativeCall(Operation,
			"The operation was cancelled before local-process materialization completed.");
		return false;
	}
}
