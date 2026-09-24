using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>Builds the copied SDK AOB outcomes and target observations that the AOB port doubles return.</summary>
internal static class AobHosts
{
	/// <summary>Gets the observation of a file opened as a process: no incarnation, the sentinel PID.</summary>
	internal static TargetSelectionFacts FileAsProcess => new(
		TargetSelectionObservationStatus.CurrentTargetFileAsProcess, TargetBackend.FileAsProcess, -1, null);

	/// <summary>Gets the observation of a CEServer target: a PID without a local incarnation.</summary>
	internal static TargetSelectionFacts Remote => new(
		TargetSelectionObservationStatus.CurrentTargetRemoteBackend, TargetBackend.CEServer, 42, null);

	/// <summary>Returns a qualified local selection (PID 42 by default).</summary>
	internal static TargetSelectionFacts Local(int processId = 42, long startedAtUtcTicks = 1_000)
	{
		return new TargetSelectionFacts(TargetSelectionObservationStatus.CurrentTargetQualified,
			TargetBackend.LocalProcess, processId, TargetObservations.Incarnation(processId, startedAtUtcTicks));
	}

	/// <summary>Returns a global outcome whose target observations both denote the same local incarnation.</summary>
	internal static AobHostOutcome Outcome(AobScanOutcomeKind kind, int resultCount = 0)
	{
		return Outcome(kind, Local(), Local(), resultCount);
	}

	/// <summary>Returns a global outcome with explicit target observations.</summary>
	internal static AobHostOutcome Outcome(AobScanOutcomeKind kind, TargetSelectionFacts before,
		TargetSelectionFacts after, int resultCount = 0)
	{
		LuaStatus luaStatus = kind == AobScanOutcomeKind.ProtectedLuaFailure ? LuaStatus.RuntimeError : LuaStatus.Ok;
		return new AobHostOutcome(kind, luaStatus, resultCount, before, after);
	}
}

/// <summary>
///     A configurable <see cref="IAobScanPort" />. By default it hands out its list with the outcome the SDK reports for
///     that list (<c>Matches</c>, or <c>NoMatches</c> for an empty list) on a qualified local target, and reports
///     <c>InvalidResult</c> without a list.
/// </summary>
internal sealed class FakeAobScanPort(RecordingAobMatchList? matchList = null) : IAobScanPort
{
	internal ModuleInfo[] Modules
	{
		get;
		init;
	} = [];

	internal int? ReportedModuleCount
	{
		get;
		init;
	}

	/// <summary>Gets the outcome to report instead of the one derived from the list.</summary>
	internal AobHostOutcome? Outcome
	{
		get;
		init;
	}

	internal Action? OnScan
	{
		get;
		init;
	}

	internal Action? OnEnumerateModules
	{
		get;
		init;
	}

	internal int EnumerationCalls
	{
		get;
		private set;
	}

	internal int ScanCalls
	{
		get;
		private set;
	}

	internal int? EnumerationCallsWhenScanStarted
	{
		get;
		private set;
	}

	internal AobScanOptions? LastOptions
	{
		get;
		private set;
	}

	public AobHostOutcome TryScan(string pattern, AobScanOptions options, out IAobMatchList? matches)
	{
		ScanCalls++;
		LastOptions = options;
		EnumerationCallsWhenScanStarted ??= EnumerationCalls;
		OnScan?.Invoke();
		matches = matchList;
		return Outcome ?? (matchList is null
			? AobHosts.Outcome(AobScanOutcomeKind.InvalidResult)
			: AobHosts.Outcome(matchList.Count == 0 ? AobScanOutcomeKind.NoMatches : AobScanOutcomeKind.Matches,
				matchList.Count));
	}

	public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
	{
		EnumerationCalls++;
		OnEnumerateModules?.Invoke();
		Array.Copy(Modules, destination, Math.Min(Modules.Length, destination.Length));
		written = ReportedModuleCount ?? Modules.Length;
		return InspectionStatus.Success;
	}
}

/// <summary>An in-memory AOB result list that records its reads and its single release.</summary>
internal sealed class RecordingAobMatchList(IReadOnlyList<string> items) : IAobMatchList
{
	internal int Count => items.Count;

	internal Action<int>? OnTryGetItem
	{
		get;
		init;
	}

	/// <summary>Gets the SDK status the release reports; <see cref="TargetReleaseStatus.Released" /> by default.</summary>
	internal TargetReleaseStatus ReleaseStatus
	{
		get;
		init;
	} = TargetReleaseStatus.Released;

	/// <summary>Gets an exception the release throws, breaking the port contract.</summary>
	internal Exception? ReleaseFailure
	{
		get;
		init;
	}

	internal int? ReportedCount
	{
		get;
		init;
	}

	internal bool CountAvailable
	{
		get;
		init;
	} = true;

	internal int CountCalls
	{
		get;
		private set;
	}

	internal int ItemCalls
	{
		get;
		private set;
	}

	internal int ReleaseCount
	{
		get;
		private set;
	}

	internal int? ReleaseThreadId
	{
		get;
		private set;
	}

	internal bool IsReleased => ReleaseCount > 0;

	public bool TryGetCount(out int count)
	{
		CountCalls++;
		count = ReportedCount ?? items.Count;
		return CountAvailable;
	}

	public bool TryGetItem(int index, [NotNullWhen(true)] out string? value)
	{
		ItemCalls++;
		OnTryGetItem?.Invoke(index);
		if ((uint) index >= items.Count)
		{
			value = null;
			return false;
		}

		value = items[index];
		return true;
	}

	public TargetReleaseStatus Release()
	{
		ReleaseCount++;
		ReleaseThreadId = Environment.CurrentManagedThreadId;
		if (ReleaseFailure is not null)
		{
			throw ReleaseFailure;
		}

		return ReleaseStatus;
	}
}
