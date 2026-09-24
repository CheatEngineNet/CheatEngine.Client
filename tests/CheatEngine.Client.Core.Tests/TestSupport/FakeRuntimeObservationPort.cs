using CheatEngine.Client.Core.Domains;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>
///     A configurable Cheat Engine for the observation and selection ports that behaves like the CheatEngine.SDK 2.0.0
///     operations: by default Cheat Engine 7.7.0.10621 x64 on Windows with an x64 local target (PID 42).
/// </summary>
/// <remarks>
///     Unless <see cref="RuntimeInfoStatus" /> or <see cref="Capabilities" /> is set, the aggregate snapshot follows the
///     SDK's rules: a failed host read fails it, a selected or absent target is a snapshot, any other target status is
///     returned unchanged, and the capability list names each probed fact. The selection observation follows the target
///     unless <see cref="SelectionStatus" /> is set: a local target is qualified when it has an
///     <see cref="Incarnation" />. <c>SelectAndObserve</c> selects the requested process unless <see cref="OnSelect" />
///     says what Cheat Engine selects instead.
/// </remarks>
internal class FakeRuntimeObservationPort : TargetObservationDouble, IRuntimeObservationPort, IProcessSelectionPort
{
	private Exception? _fault;

	/// <summary>Gets the host facts of Cheat Engine 7.7.0.10621 x64 on Windows.</summary>
	internal static CheatEngineHostObservation DefaultHost => new(CheatEngineVersion.Ce77010621,
		CheatEngineArchitecture.X64, true, CheatEngineOperatingSystem.Windows);

	/// <summary>Gets or sets the host facts.</summary>
	internal CheatEngineHostObservation Host
	{
		get;
		set;
	} = DefaultHost;

	/// <summary>Gets or sets the status of the aggregate host observation.</summary>
	internal LuaOperationStatus HostStatus
	{
		get;
		set;
	} = LuaOperationStatus.Success;

	/// <summary>Gets or sets the status of the file-version read.</summary>
	internal LuaOperationStatus FileVersionStatus
	{
		get;
		set;
	} = LuaOperationStatus.Success;

	/// <summary>Gets or sets the status of the system-architecture read.</summary>
	internal LuaOperationStatus SystemArchitectureStatus
	{
		get;
		set;
	} = LuaOperationStatus.Success;

	/// <summary>Gets or sets the status of the Cheat Engine bitness read.</summary>
	internal LuaOperationStatus CheatEngineBitnessStatus
	{
		get;
		set;
	} = LuaOperationStatus.Success;

	/// <summary>Gets or sets the status of the operating-system read.</summary>
	internal LuaOperationStatus OperatingSystemStatus
	{
		get;
		set;
	} = LuaOperationStatus.Success;

	/// <summary>Gets or sets the status of the aggregate snapshot; derived from the host and target when unset.</summary>
	internal ProcessOperationStatus? RuntimeInfoStatus
	{
		get;
		set;
	}

	/// <summary>Gets or sets the SDK capability list of the snapshot; derived from the facts when unset.</summary>
	internal RuntimeCapabilities? Capabilities
	{
		get;
		set;
	}

	/// <summary>Gets or sets an exception every member, target observations included, throws.</summary>
	internal Exception? Fault
	{
		get => _fault;
		set
		{
			_fault = value;
			TargetFault = value;
		}
	}

	/// <summary>Gets the names of the host, snapshot, selection and attach operations in call order.</summary>
	internal List<string> Calls
	{
		get;
	} = [];

	/// <summary>Gets or sets whether CheatEngine.SDK detected an external Lua state reset.</summary>
	public bool ExternalStateResetDetected
	{
		get;
		set;
	}

	/// <summary>Gets or sets the incarnation of a local target; <see langword="null" /> leaves it unqualified.</summary>
	internal TargetProcessIncarnation? Incarnation
	{
		get;
		set;
	}

	/// <summary>Gets or sets the status of the selection observation; derived from the target when unset.</summary>
	internal TargetSelectionObservationStatus? SelectionStatus
	{
		get;
		set;
	}

	/// <summary>Gets or sets the status of <c>SelectAndObserve</c>; derived from the resulting selection when unset.</summary>
	internal ProcessOperationStatus? SelectStatus
	{
		get;
		set;
	}

	/// <summary>Gets or sets what Cheat Engine does when asked to select a PID; it selects that PID when unset.</summary>
	internal Action<int>? OnSelect
	{
		get;
		set;
	}

	/// <summary>Gets or sets an exception <c>SelectAndObserve</c> throws.</summary>
	internal Exception? SelectFault
	{
		get;
		set;
	}

	/// <summary>Gets the PIDs <c>SelectAndObserve</c> was asked to select.</summary>
	internal List<int> SelectCalls
	{
		get;
	} = [];

	public ProcessOperationStatus TryObserveRuntimeInfo(out RuntimeInfo? info)
	{
		Record(nameof(TryObserveRuntimeInfo));
		ProcessOperationStatus status = RuntimeInfoStatus ?? DeriveRuntimeInfoStatus();
		info = status.IsSuccess
			? new RuntimeInfo(Host, TargetStatus.IsSuccess ? Target : null, Capabilities ?? DeriveCapabilities())
			: null;
		return status;
	}

	public LuaOperationStatus ObserveHost(out CheatEngineHostObservation host)
	{
		Record(nameof(ObserveHost));
		host = HostStatus.IsSuccess ? Host : default;
		return HostStatus;
	}

	public LuaOperationStatus TryGetCheatEngineFileVersion(out CheatEngineVersion version)
	{
		Record(nameof(TryGetCheatEngineFileVersion));
		version = FileVersionStatus.IsSuccess ? Host.FileVersion.GetValueOrDefault() : default;
		return FileVersionStatus;
	}

	public LuaOperationStatus TryGetSystemArchitecture(out CheatEngineArchitecture architecture)
	{
		Record(nameof(TryGetSystemArchitecture));
		architecture = SystemArchitectureStatus.IsSuccess ? Host.SystemArchitecture : CheatEngineArchitecture.Unknown;
		return SystemArchitectureStatus;
	}

	public LuaOperationStatus TryIsCheatEngine64Bit(out bool is64Bit)
	{
		Record(nameof(TryIsCheatEngine64Bit));
		is64Bit = CheatEngineBitnessStatus.IsSuccess && Host.CheatEngineIs64Bit == true;
		return CheatEngineBitnessStatus;
	}

	public LuaOperationStatus TryGetOperatingSystem(out CheatEngineOperatingSystem operatingSystem)
	{
		Record(nameof(TryGetOperatingSystem));
		operatingSystem = OperatingSystemStatus.IsSuccess ? Host.OperatingSystem : CheatEngineOperatingSystem.Unknown;
		return OperatingSystemStatus;
	}

	public TargetSelectionFacts ObserveSelection()
	{
		Record(nameof(ObserveSelection));
		return CurrentSelection();
	}

	public TargetIdentityFacts ValidateSelection(TargetProcessIncarnation expected)
	{
		Record(nameof(ValidateSelection));
		TargetSelectionFacts observed = CurrentSelection();
		TargetIdentityCheckKind kind = observed is
		{
			Status: TargetSelectionObservationStatus.CurrentTargetQualified, Incarnation: { } current
		}
			? current.ProcessId != expected.ProcessId
				? TargetIdentityCheckKind.TargetChanged
				: current.StartedAtUtcTicks == expected.StartedAtUtcTicks
					? TargetIdentityCheckKind.Current
					: TargetIdentityCheckKind.ProcessReused
			: observed.Status switch
			{
				TargetSelectionObservationStatus.NoTargetSelected => TargetIdentityCheckKind.NoTargetSelected,
				TargetSelectionObservationStatus.CurrentTargetUnqualified =>
					TargetIdentityCheckKind.CurrentTargetUnqualified,
				TargetSelectionObservationStatus.GlobalUnavailable => TargetIdentityCheckKind.GlobalUnavailable,
				TargetSelectionObservationStatus.LuaFailure => TargetIdentityCheckKind.LuaFailure,
				TargetSelectionObservationStatus.CurrentTargetRemoteBackend => TargetIdentityCheckKind.RemoteBackend,
				TargetSelectionObservationStatus.CurrentTargetFileAsProcess => TargetIdentityCheckKind.FileAsProcess,
				TargetSelectionObservationStatus.CurrentTargetBackendUnknown => TargetIdentityCheckKind.BackendUnknown,
				_ => TargetIdentityCheckKind.InvalidResult
			};
		return new TargetIdentityFacts(kind, observed);
	}

	public ProcessOperationStatus SelectAndObserve(TargetProcessId processId,
		out CurrentProcessObservation observation)
	{
		Record(nameof(SelectAndObserve));
		SelectCalls.Add(processId.Value);
		if (SelectFault is { } fault)
		{
			throw fault;
		}

		if (OnSelect is { } select)
		{
			select(processId.Value);
		}
		else
		{
			TargetStatus = ProcessOperationStatus.Success;
			Target = TargetObservations.WithProcessId(Target, processId.Value);
		}

		ProcessOperationStatus status = SelectStatus ?? (TargetStatus.IsSuccess && Target.ProcessId == processId
			? ProcessOperationStatus.Success
			: ProcessOperationStatus.SelectionNotConfirmed);
		observation = status.IsSuccess ? new CurrentProcessObservation(processId, Target.Bitness) : default;
		return status;
	}

	/// <summary>Counts the calls of one host, snapshot, selection or attach operation.</summary>
	internal int Count(string member)
	{
		return Calls.Count(call => call == member);
	}

	private ProcessOperationStatus DeriveRuntimeInfoStatus()
	{
		if (!HostStatus.IsSuccess)
		{
			return HostStatus.Kind switch
			{
				LuaOperationStatusKind.LuaFailure => TargetObservations.LuaFailure,
				LuaOperationStatusKind.GlobalUnavailable => ProcessOperationStatus.GlobalUnavailable,
				_ => ProcessOperationStatus.InvalidResult
			};
		}

		return TargetStatus.Kind is ProcessOperationStatusKind.Success or ProcessOperationStatusKind.TargetNotAttached
			or ProcessOperationStatusKind.GlobalUnavailable
			? ProcessOperationStatus.Success
			: TargetStatus;
	}

	private RuntimeCapabilities DeriveCapabilities()
	{
		List<RuntimeCapabilityAvailability> entries =
		[
			Available(RuntimeCapabilityId.CheatEngineVersion),
			Available(RuntimeCapabilityId.SystemArchitecture),
			Available(RuntimeCapabilityId.CheatEngineBitness),
			Available(RuntimeCapabilityId.OperatingSystem)
		];
		switch (TargetStatus.Kind)
		{
			case ProcessOperationStatusKind.Success:
				entries.AddRange(
				[
					Available(RuntimeCapabilityId.CurrentProcess), Available(RuntimeCapabilityId.TargetBackend),
					Available(RuntimeCapabilityId.TargetArchitecture), Available(RuntimeCapabilityId.TargetAndroid),
					Available(RuntimeCapabilityId.TargetAbi), Available(RuntimeCapabilityId.ConfiguredPointerSize)
				]);
				break;
			case ProcessOperationStatusKind.TargetNotAttached:
				entries.Add(Available(RuntimeCapabilityId.CurrentProcess));
				break;
			case ProcessOperationStatusKind.GlobalUnavailable:
				entries.Add(new RuntimeCapabilityAvailability(RuntimeCapabilityId.CurrentProcess,
					RuntimeCapabilityAvailabilityState.Unavailable, RuntimeCapabilityContract.Unknown));
				break;
		}

		return RuntimeCapabilities.Create([.. entries]);
	}

	private TargetSelectionFacts CurrentSelection()
	{
		TargetSelectionObservationStatus status = SelectionStatus ?? TargetStatus.Kind switch
		{
			ProcessOperationStatusKind.TargetNotAttached => TargetSelectionObservationStatus.NoTargetSelected,
			ProcessOperationStatusKind.FileAsProcessTarget => TargetSelectionObservationStatus.CurrentTargetFileAsProcess,
			_ => Target.Backend switch
			{
				TargetBackend.LocalProcess => Incarnation is null
					? TargetSelectionObservationStatus.CurrentTargetUnqualified
					: TargetSelectionObservationStatus.CurrentTargetQualified,
				TargetBackend.CEServer => TargetSelectionObservationStatus.CurrentTargetRemoteBackend,
				_ => TargetSelectionObservationStatus.CurrentTargetBackendUnknown
			}
		};
		TargetBackend backend = status switch
		{
			TargetSelectionObservationStatus.CurrentTargetQualified or
				TargetSelectionObservationStatus.CurrentTargetUnqualified => TargetBackend.LocalProcess,
			TargetSelectionObservationStatus.CurrentTargetRemoteBackend => TargetBackend.CEServer,
			TargetSelectionObservationStatus.CurrentTargetFileAsProcess => TargetBackend.FileAsProcess,
			_ => TargetBackend.Unknown
		};
		int? processId = status is TargetSelectionObservationStatus.CurrentTargetQualified
			or TargetSelectionObservationStatus.CurrentTargetUnqualified
			or TargetSelectionObservationStatus.CurrentTargetRemoteBackend
			or TargetSelectionObservationStatus.CurrentTargetBackendUnknown
			? Target.ProcessId.Value
			: null;
		return new TargetSelectionFacts(status, backend, processId,
			status == TargetSelectionObservationStatus.CurrentTargetQualified ? Incarnation : null);
	}

	private static RuntimeCapabilityAvailability Available(RuntimeCapabilityId capability)
	{
		return new RuntimeCapabilityAvailability(capability, RuntimeCapabilityAvailabilityState.Available,
			RuntimeCapabilityContract.Unknown);
	}

	private void Record(string member)
	{
		Calls.Add(member);
		if (Fault is { } fault)
		{
			throw fault;
		}
	}
}
