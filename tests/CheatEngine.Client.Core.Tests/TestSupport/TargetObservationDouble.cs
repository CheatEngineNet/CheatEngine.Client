using CheatEngine.Client.Core.Domains;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>Builds copied CheatEngine.SDK target observations for test doubles.</summary>
internal static class TargetObservations
{
	/// <summary>The configured pointer size that equals the bitness of the built observation.</summary>
	internal const int SameAsBitness = int.MinValue;

	/// <summary>A protected Lua failure status, as the SDK reports a raising global.</summary>
	internal static ProcessOperationStatus LuaFailure =>
		ProcessOperationStatus.ProtectedLuaFailure(LuaStatus.RuntimeError);

	/// <summary>Returns the SDK status of a kind, as the SDK constructs it.</summary>
	internal static ProcessOperationStatus Status(ProcessOperationStatusKind kind)
	{
		return kind switch
		{
			ProcessOperationStatusKind.Success => ProcessOperationStatus.Success,
			ProcessOperationStatusKind.TargetNotAttached => ProcessOperationStatus.TargetNotAttached,
			ProcessOperationStatusKind.SelectionNotConfirmed => ProcessOperationStatus.SelectionNotConfirmed,
			ProcessOperationStatusKind.GlobalUnavailable => ProcessOperationStatus.GlobalUnavailable,
			ProcessOperationStatusKind.ProtectedLuaFailure => TargetObservations.LuaFailure,
			ProcessOperationStatusKind.InvalidResult => ProcessOperationStatus.InvalidResult,
			ProcessOperationStatusKind.TargetChanged => ProcessOperationStatus.TargetChanged,
			ProcessOperationStatusKind.FileAsProcessTarget => ProcessOperationStatus.FileAsProcessTarget,
			_ => default
		};
	}

	/// <summary>Builds the facts Cheat Engine reports for one selected target (an x64 local process by default).</summary>
	internal static TargetArchitectureObservation Create(
		int processId = 42,
		bool is64Bit = true,
		bool? isX86Family = true,
		bool? isArmFamily = false,
		int? configuredPointerSizeBytes = SameAsBitness,
		TargetBackend backend = TargetBackend.LocalProcess,
		bool? isAndroid = false,
		int? abiCode = 0)
	{
		PointerSize bitness = is64Bit ? PointerSize.Bit64 : PointerSize.Bit32;
		int? configured = configuredPointerSizeBytes == SameAsBitness ? bitness.Bytes : configuredPointerSizeBytes;
		return new TargetArchitectureObservation(new TargetProcessId(processId), backend, bitness, isX86Family,
			isArmFamily, isAndroid, abiCode, configured);
	}
}

/// <summary>
///     A configurable <see cref="ITargetObservationPort" />: by default an x64 local target (PID 42) whose configured
///     pointer size equals its bitness. The narrowed reads follow the same target unless a sequence is configured.
/// </summary>
internal class TargetObservationDouble : ITargetObservationPort
{
	private int _currentReads;

	/// <summary>Gets or sets the status of the full target observation.</summary>
	internal ProcessOperationStatus TargetStatus
	{
		get;
		set;
	} = ProcessOperationStatus.Success;

	/// <summary>Gets or sets the facts of the selected target.</summary>
	internal TargetArchitectureObservation Target
	{
		get;
		set;
	} = TargetObservations.Create();

	/// <summary>Gets or sets an exception every target observation throws (for example a detached SDK runtime).</summary>
	internal Exception? TargetFault
	{
		get;
		set;
	}

	/// <summary>
	///     Gets or sets the results of successive <c>ObserveCurrent</c> reads; the last one repeats. When unset, the read
	///     reports the target of <see cref="Target" /> unless <see cref="TargetStatus" /> says why no process is selected.
	/// </summary>
	internal (ProcessOperationStatus Status, int ProcessId)[]? CurrentReads
	{
		get;
		set;
	}

	/// <summary>Gets or sets the status of <c>TryGetConfiguredPointerSize</c>; derived from the target when unset.</summary>
	internal ProcessOperationStatus? ConfiguredStatus
	{
		get;
		set;
	}

	/// <summary>Gets the names of the target observations in call order.</summary>
	internal List<string> TargetCalls
	{
		get;
	} = [];

	public ProcessOperationStatus ObserveTargetArchitecture(out TargetArchitectureObservation observation)
	{
		RecordTarget(nameof(ObserveTargetArchitecture));
		observation = TargetStatus.IsSuccess ? Target : default;
		return TargetStatus;
	}

	public ProcessOperationStatus ObserveCurrent(out CurrentProcessObservation observation)
	{
		RecordTarget(nameof(ObserveCurrent));
		ProcessOperationStatus status;
		int processId;
		if (CurrentReads is { Length: > 0 } reads)
		{
			(status, processId) = reads[Math.Min(_currentReads, reads.Length - 1)];
		}
		else
		{
			status = TargetStatus.Kind is ProcessOperationStatusKind.ProtectedLuaFailure
				or ProcessOperationStatusKind.InvalidResult
				? ProcessOperationStatus.Success
				: TargetStatus;
			processId = Target.ProcessId.Value;
		}

		_currentReads++;
		observation = status.IsSuccess
			? new CurrentProcessObservation(new TargetProcessId(processId), Target.Bitness)
			: default;
		return status;
	}

	public ProcessOperationStatus TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize)
	{
		RecordTarget(nameof(TryGetConfiguredPointerSize));
		rawBytes = Target.ConfiguredPointerSizeBytes.GetValueOrDefault();
		pointerSize = Target.ConfiguredPointerSize;
		return ConfiguredStatus ?? (Target.ConfiguredPointerSizeBytes switch
		{
			null => ProcessOperationStatus.GlobalUnavailable,
			4 or 8 => ProcessOperationStatus.Success,
			_ => ProcessOperationStatus.InvalidResult
		});
	}

	/// <summary>Counts the calls of one target observation.</summary>
	internal int CountTarget(string member)
	{
		return TargetCalls.Count(call => call == member);
	}

	private void RecordTarget(string member)
	{
		TargetCalls.Add(member);
		if (TargetFault is { } fault)
		{
			throw fault;
		}
	}
}
