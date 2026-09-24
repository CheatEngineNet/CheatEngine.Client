using CheatEngine.Client.Core.Domains;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>
///     A configurable <see cref="IRuntimeObservationPort" /> that behaves like the CheatEngine.SDK 2.0.0 operations:
///     by default Cheat Engine 7.7.0.10621 x64 on Windows with an x64 local target (PID 42).
/// </summary>
/// <remarks>
///     Unless <see cref="RuntimeInfoStatus" /> or <see cref="Capabilities" /> is set, the aggregate snapshot follows the
///     SDK's rules: a failed host read fails it, a selected or absent target is a snapshot, any other target status is
///     returned unchanged, and the capability list names each probed fact.
/// </remarks>
internal class FakeRuntimeObservationPort : TargetObservationDouble, IRuntimeObservationPort
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

	/// <summary>Gets the names of the host and snapshot operations in call order.</summary>
	internal List<string> Calls
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

	/// <summary>Counts the calls of one host or snapshot operation.</summary>
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
