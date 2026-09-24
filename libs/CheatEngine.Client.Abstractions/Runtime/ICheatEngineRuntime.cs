using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Runtime;

/// <summary>Reads immutable runtime facts and capability observations for the active Cheat Engine activation.</summary>
/// <remarks>
///     <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add members
///     to it, so implement it only in a test double.
/// </remarks>
public interface ICheatEngineRuntime
{
	/// <summary>Gets the activation epoch for which this runtime service is valid.</summary>
	public long Epoch
	{
		get;
	}

	/// <summary>Tries to capture the runtime facts available to the active plugin.</summary>
	public bool TryGetSnapshot(
		out CheatEngineRuntimeSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Captures the runtime facts or throws when they are unavailable.</summary>
	public CheatEngineRuntimeSnapshot GetSnapshot(CancellationToken cancellationToken = default);

	/// <summary>Reads one SDK capability observation without exposing its backing Lua global.</summary>
	public bool TryGetSdkCapability(
		RuntimeCapabilityId capability,
		out RuntimeCapabilityAvailability availability,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Reads one SDK capability observation or throws when it cannot be observed.</summary>
	public RuntimeCapabilityAvailability GetSdkCapability(
		RuntimeCapabilityId capability,
		CancellationToken cancellationToken = default);

	/// <summary>Reads one Client capability observation without exposing internal adapters or handles.</summary>
	public bool TryGetClientCapability(
		ClientCapabilityId capability,
		out ClientCapabilityAvailability availability,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Reads one Client capability observation or throws when it cannot be observed.</summary>
	public ClientCapabilityAvailability GetClientCapability(
		ClientCapabilityId capability,
		CancellationToken cancellationToken = default);
}
