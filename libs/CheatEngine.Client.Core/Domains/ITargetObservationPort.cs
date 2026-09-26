using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Internal, read-only port for the facts Cheat Engine reports about its selected target: the process identifier,
///     the bitness, the backend, the ISA families, the ABI and the configured pointer size.
/// </summary>
/// <remarks>
///     <para>
///         Each member is one CheatEngine.SDK 2.0.0 <c>RuntimeProcessOperations</c> observation, run in its own Lua
///         admission. The SDK reads the selected process identifier before and after the facts and reads no fact when
///         no target, or the file-as-process sentinel, is selected; the Client does not bracket, derive or re-read
///         anything itself. A member never selects, opens, pauses or configures a target and never changes the configured
///         pointer size (audit ADR-09a, Q45).
///     </para>
///     <para>
///         The Runtime, Processes and Memory domains share this port through <see cref="TargetArchitectureObserver" />,
///         so one target observation policy serves every domain. Test doubles implement it; production uses
///         <see cref="SdkRuntimeObservationPort" />.
///     </para>
/// </remarks>
internal interface ITargetObservationPort
{
	/// <summary>Observes the selected process identifier and its bitness (<c>RuntimeProcessOperations.ObserveCurrent</c>).</summary>
	public ProcessOperationStatus ObserveCurrent(out CurrentProcessObservation observation);

	/// <summary>
	///     Observes every fact about the selected target between two selected-PID reads
	///     (<c>RuntimeProcessOperations.ObserveTargetArchitecture</c>).
	/// </summary>
	public ProcessOperationStatus ObserveTargetArchitecture(out TargetArchitectureObservation observation);

	/// <summary>
	///     Reads the configured pointer size between two selected-PID reads
	///     (<c>RuntimeProcessOperations.TryGetConfiguredPointerSize</c>); the raw value is kept for any integer.
	/// </summary>
	public ProcessOperationStatus TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize);
}
