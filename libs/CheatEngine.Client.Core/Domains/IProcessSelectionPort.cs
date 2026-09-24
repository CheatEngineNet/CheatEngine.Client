using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal port for the one Cheat Engine call of the Processes domain that changes the selected target.</summary>
/// <remarks>
///     <para>
///         <see cref="SelectAndObserve" /> is CheatEngine.SDK 2.0.0 <c>RuntimeProcessOperations.SelectAndObserve</c>:
///         <c>openProcess</c>, then the selected PID and bitness in the same Lua admission. A normal return of
///         <c>openProcess</c> is not success by itself: success requires the next selected-PID read to equal the
///         requested process. Selecting a process also resets Cheat Engine's configured pointer size.
///     </para>
///     <para>
///         It is kept apart from <see cref="IRuntimeObservationPort" />, which only observes, and only
///         <c>ProcessClient.TryAttach</c> calls it (architecture ratchet).
///     </para>
/// </remarks>
internal interface IProcessSelectionPort
{
	/// <summary>Selects an explicit process and verifies Cheat Engine's resulting selection.</summary>
	public ProcessOperationStatus SelectAndObserve(TargetProcessId processId,
		out CurrentProcessObservation observation);
}
