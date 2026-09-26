using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Observes the facts of one runtime snapshot through CheatEngine.SDK 2.0.0, keeping every fact that can be
///     established when another one cannot (audit ADR-09, F08).
/// </summary>
/// <remarks>
///     <para>
///         The first observation is <c>RuntimeObservations.TryObserveRuntimeInfo</c>: the host facts, then the target
///         facts, in one Lua admission, with the SDK's own capability observations. No selected target is a legitimate
///         snapshot, so is an absent global.
///     </para>
///     <para>
///         The SDK produces no snapshot for a file opened as a process, a target change, or a host or target global that
///         raised, returned <c>nil</c> or returned a malformed value. The observer then reads the host facts with
///         <c>RuntimeHostOperations.ObserveHost</c>, and each fact alone when that fails too, and the target through
///         <see cref="TargetArchitectureObserver" />. The host evidence of process selection then comes from the target
///         observation's status (<see cref="RuntimeObservationMapping.ToHostEvidenceState(ProcessOperationStatusKind)" />)
///         instead of the SDK's capability list.
///     </para>
/// </remarks>
internal static class RuntimeObserver
{
	/// <summary>Observes the host and the selected target.</summary>
	/// <param name="port">The read-only runtime observation port.</param>
	/// <returns>The copied facts.</returns>
	internal static ObservedRuntime Observe(IRuntimeObservationPort port)
	{
		ArgumentNullException.ThrowIfNull(port);
		ProcessOperationStatus status = port.TryObserveRuntimeInfo(out RuntimeInfo? info);
		if (status.IsSuccess && info is { Host: { } host })
		{
			// Without target facts the SDK reports either no selected target or an absent required global
			// (getOpenedProcessID or targetIs64Bit), which its capability list records as unavailable.
			ObservedTarget target = info.Target is { } facts
				? new ObservedTarget(ProcessOperationStatus.Success, facts, null)
				: new ObservedTarget(
					info.Capabilities.GetState(RuntimeCapabilityId.CurrentProcess) ==
					RuntimeCapabilityAvailabilityState.Unavailable ||
					info.Capabilities.GetState(RuntimeCapabilityId.TargetArchitecture) ==
					RuntimeCapabilityAvailabilityState.Unavailable
						? ProcessOperationStatus.GlobalUnavailable
						: ProcessOperationStatus.TargetNotAttached, default, null);
			return new ObservedRuntime(status, host, target, FromSdkCapability(info.Capabilities));
		}

		ObservedTarget observed = TargetArchitectureObserver.Observe(port);
		return new ObservedRuntime(status, ObserveHost(port), observed,
			FromTargetObservation(status, observed.Status));
	}

	/// <summary>Reads the host facts together, or one by one when one of them cannot be read.</summary>
	private static CheatEngineHostObservation ObserveHost(IRuntimeObservationPort port)
	{
		LuaOperationStatus status = port.ObserveHost(out CheatEngineHostObservation host);
		if (status.IsSuccess)
		{
			return host;
		}

		CheatEngineVersion? version = Keeps(port.TryGetCheatEngineFileVersion(out CheatEngineVersion fileVersion))
			? fileVersion
			: null;
		CheatEngineArchitecture architecture =
			Keeps(port.TryGetSystemArchitecture(out CheatEngineArchitecture systemArchitecture))
				? systemArchitecture
				: CheatEngineArchitecture.Unknown;
		bool? is64Bit = Keeps(port.TryIsCheatEngine64Bit(out bool cheatEngineIs64Bit)) ? cheatEngineIs64Bit : null;
		CheatEngineOperatingSystem operatingSystem =
			Keeps(port.TryGetOperatingSystem(out CheatEngineOperatingSystem reported))
				? reported
				: CheatEngineOperatingSystem.Unknown;
		return new CheatEngineHostObservation(version, architecture, is64Bit, operatingSystem);
	}

	private static bool Keeps(LuaOperationStatus status)
	{
		return RuntimeObservationMapping.ToFactEvidenceState(status.Kind) == ClientCapabilityEvidenceState.Satisfied;
	}

	private static ClientCapabilityEvidenceGate FromSdkCapability(RuntimeCapabilities capabilities)
	{
		if (!capabilities.TryGet(RuntimeCapabilityId.CurrentProcess, out RuntimeCapabilityAvailability availability))
		{
			return new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				"CheatEngine.SDK did not probe the selected-process primitive (Process.Current) in this snapshot.");
		}

		ClientCapabilityEvidenceState state = RuntimeObservationMapping.ToHostEvidenceState(availability.State);
		return new ClientCapabilityEvidenceGate(state, state switch
		{
			ClientCapabilityEvidenceState.Satisfied =>
				"CheatEngine.SDK observed the selected-process primitive (Process.Current) in this snapshot.",
			ClientCapabilityEvidenceState.Missing =>
				"CheatEngine.SDK reports the selected-process primitive (Process.Current) unavailable in this host.",
			_ => "CheatEngine.SDK could not establish the selected-process primitive (Process.Current)."
		});
	}

	private static ClientCapabilityEvidenceGate FromTargetObservation(ProcessOperationStatus runtimeStatus,
		ProcessOperationStatus targetStatus)
	{
		return new ClientCapabilityEvidenceGate(RuntimeObservationMapping.ToHostEvidenceState(targetStatus.Kind),
			$"CheatEngine.SDK produced no runtime snapshot ({runtimeStatus.Kind}); the selected-process observation " +
			$"reported {targetStatus.Kind}.");
	}
}

/// <summary>The copied facts of one runtime snapshot.</summary>
/// <param name="RuntimeInfoStatus">The status of <c>RuntimeObservations.TryObserveRuntimeInfo</c>.</param>
/// <param name="Host">The host facts; a fact that could not be read is <see langword="null" /> or unknown.</param>
/// <param name="Target">The target observation.</param>
/// <param name="ProcessSelectionHost">
///     The host evidence of the selected-process primitive: the SDK's <c>Process.Current</c> capability entry, or the
///     status of the target observation when the SDK produced no snapshot.
/// </param>
internal sealed record ObservedRuntime(
	ProcessOperationStatus RuntimeInfoStatus,
	CheatEngineHostObservation Host,
	ObservedTarget Target,
	ClientCapabilityEvidenceGate ProcessSelectionHost);
