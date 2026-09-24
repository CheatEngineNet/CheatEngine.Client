using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Internal, read-only port for the Cheat Engine host and target facts of the runtime snapshot, so the Runtime and
///     Processes domains are testable without the CheatEngine.SDK statics.
/// </summary>
/// <remarks>
///     <para>
///         Every member is one read-only CheatEngine.SDK 2.0.0 operation: <c>RuntimeObservations.TryObserveRuntimeInfo</c>,
///         the <c>RuntimeHostOperations</c> and <c>RuntimeProcessOperations</c> observations, and
///         <c>TargetSelection.ObserveCurrent</c> and <c>ValidateCurrent</c>. None of them selects a
///         target, loads a driver or a table, executes code remotely or changes a host setting (audit ADR-09a, Q45); the
///         architecture ratchet (<c>RuntimeProbeCallsOnlyReadOnlySdkOperations</c>) keeps the production port to that
///         list.
///     </para>
///     <para>
///         The SDK reports every outcome as a status and never through Lua error text. An operation that cannot acquire
///         its Lua admission (for example a plugin that is not enabled) throws <see cref="InvalidOperationException" />,
///         which the calling domain classifies through <see cref="CheatEngine.Client.Core.Infrastructure.SdkBoundary" />.
///     </para>
/// </remarks>
internal interface IRuntimeObservationPort : ITargetObservationPort
{
	/// <summary>Observes the host and, when one is selected, the target in one Lua admission.</summary>
	public ProcessOperationStatus TryObserveRuntimeInfo(out RuntimeInfo? info);

	/// <summary>Observes the four host facts in one Lua admission (<c>RuntimeHostOperations.ObserveHost</c>).</summary>
	public LuaOperationStatus ObserveHost(out CheatEngineHostObservation host);

	/// <summary>Reads the complete Cheat Engine file version (<c>getCheatEngineFileVersion</c>).</summary>
	public LuaOperationStatus TryGetCheatEngineFileVersion(out CheatEngineVersion version);

	/// <summary>Reads the Cheat Engine host architecture (<c>getSystemArchitecture</c>).</summary>
	public LuaOperationStatus TryGetSystemArchitecture(out CheatEngineArchitecture architecture);

	/// <summary>Reads whether the Cheat Engine process is 64-bit (<c>cheatEngineIs64Bit</c>).</summary>
	public LuaOperationStatus TryIsCheatEngine64Bit(out bool is64Bit);

	/// <summary>Reads the operating system Cheat Engine runs on (<c>getOperatingSystem</c>).</summary>
	public LuaOperationStatus TryGetOperatingSystem(out CheatEngineOperatingSystem operatingSystem);

	/// <summary>
	///     Observes what identifies the selected target (<c>TargetSelection.ObserveCurrent</c>): its PID, its backend and,
	///     for a local process, its incarnation (PID and observed creation time).
	/// </summary>
	public TargetSelectionFacts ObserveSelection();

	/// <summary>
	///     Checks whether the current selection still denotes <paramref name="expected" />
	///     (<c>TargetSelection.ValidateCurrent</c>): current, a different PID, the same PID reused by another process, or
	///     no comparable incarnation.
	/// </summary>
	public TargetIdentityFacts ValidateSelection(TargetProcessIncarnation expected);
}
