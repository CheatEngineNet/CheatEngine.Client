using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production runtime observation port: each member calls one read-only CheatEngine.SDK 2.0.0 operation.</summary>
/// <remarks>
///     <para>
///         The members copy the SDK's typed results unchanged; the Client neither re-reads a global nor derives a fact the
///         SDK did not report. The callers run them on Cheat Engine's main thread through the dispatcher.
///     </para>
///     <para>
///         This type is the only Client code that references <c>RuntimeObservations</c>, <c>RuntimeHostOperations</c>,
///         <c>TargetSelection</c> and the read-only members of <c>RuntimeProcessOperations</c>; the architecture ratchet
///         (<c>RuntimeProbeCallsOnlyReadOnlySdkOperations</c>) keeps it to read-only operations and away from
///         <c>CheatTableFiles</c> (Q45). No hosted test has a Cheat Engine process or Lua state, so its lines are excluded
///         from the coverage metric; <c>SdkRuntimeObservationPortTests</c> proves that each member requires an enabled
///         plugin.
///     </para>
/// </remarks>
internal sealed class SdkRuntimeObservationPort : IRuntimeObservationPort
{
	private SdkRuntimeObservationPort()
	{
	}

	/// <summary>Gets the stateless production port.</summary>
	internal static SdkRuntimeObservationPort Instance
	{
		get;
	} = new();

	public ProcessOperationStatus TryObserveRuntimeInfo(out RuntimeInfo? info)
	{
		return RuntimeObservations.TryObserveRuntimeInfo(out info);
	}

	public LuaOperationStatus ObserveHost(out CheatEngineHostObservation host)
	{
		return RuntimeHostOperations.ObserveHost(out host);
	}

	public LuaOperationStatus TryGetCheatEngineFileVersion(out CheatEngineVersion version)
	{
		return RuntimeHostOperations.TryGetCheatEngineFileVersion(out version);
	}

	public LuaOperationStatus TryGetSystemArchitecture(out CheatEngineArchitecture architecture)
	{
		return RuntimeHostOperations.TryGetSystemArchitecture(out architecture);
	}

	public LuaOperationStatus TryIsCheatEngine64Bit(out bool is64Bit)
	{
		return RuntimeHostOperations.TryIsCheatEngine64Bit(out is64Bit);
	}

	public LuaOperationStatus TryGetOperatingSystem(out CheatEngineOperatingSystem operatingSystem)
	{
		return RuntimeHostOperations.TryGetOperatingSystem(out operatingSystem);
	}

	public ProcessOperationStatus ObserveCurrent(out CurrentProcessObservation observation)
	{
		return RuntimeProcessOperations.ObserveCurrent(out observation);
	}

	public ProcessOperationStatus ObserveTargetArchitecture(out TargetArchitectureObservation observation)
	{
		return RuntimeProcessOperations.ObserveTargetArchitecture(out observation);
	}

	public ProcessOperationStatus TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize)
	{
		return RuntimeProcessOperations.TryGetConfiguredPointerSize(out rawBytes, out pointerSize);
	}

	public TargetSelectionFacts ObserveSelection()
	{
		return Copy(TargetSelection.ObserveCurrent());
	}

	public TargetIdentityFacts ValidateSelection(TargetProcessIncarnation expected)
	{
		TargetIdentityCheck check = TargetSelection.ValidateCurrent(expected);
		return new TargetIdentityFacts(check.Kind, Copy(check.Observed));
	}

	private static TargetSelectionFacts Copy(TargetSelectionObservation observation)
	{
		return new TargetSelectionFacts(observation.Status, observation.Backend, observation.SelectedProcessId,
			observation.Incarnation);
	}
}
