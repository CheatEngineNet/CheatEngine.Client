using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     The one target observation policy of the Runtime, Processes and Memory domains, over the read-only
///     CheatEngine.SDK 2.0.0 process operations (audit F08, A10-17, Q31, Q32).
/// </summary>
/// <remarks>
///     <para>
///         The observation is <c>RuntimeProcessOperations.ObserveTargetArchitecture</c>: the SDK reads the selected PID,
///         the backend, the bitness, the ISA families, the Android and ABI facts and the configured pointer size, then the
///         selected PID again, in one Lua admission. It reads no fact when no target or the file-as-process sentinel is
///         selected, and it reports a different closing PID as a target change. The ISA is the SDK's own derivation
///         (<see cref="TargetArchitectureObservation.Architecture" />), never a Client inference from the 64-bit fact.
///     </para>
///     <para>
///         When that observation raises a protected Lua error or returns a malformed value, one of its facts is broken
///         but the others may not be. The observer then narrows: <c>ObserveCurrent</c> (PID and bitness),
///         <c>TryGetConfiguredPointerSize</c>, and <c>ObserveCurrent</c> again. The narrowed facts are kept only when
///         both PID reads succeed and agree; a different, absent or file-as-process closing selection is a target change.
///         The narrowed observation leaves the backend, the ISA, the Android and ABI facts unknown. Like the SDK's own
///         bracket, the two reads do not detect a selection that changed and changed back between them.
///     </para>
///     <para>
///         Every call is an observation: nothing selects, opens, pauses or configures a target (Q45).
///     </para>
/// </remarks>
internal static class TargetArchitectureObserver
{
	/// <summary>Observes the selected target, narrowing to the facts that can still be read when one fact is broken.</summary>
	/// <param name="port">The read-only target observation port.</param>
	/// <returns>The copied observation and its status.</returns>
	internal static ObservedTarget Observe(ITargetObservationPort port)
	{
		ArgumentNullException.ThrowIfNull(port);
		ProcessOperationStatus status = port.ObserveTargetArchitecture(out TargetArchitectureObservation facts);
		if (status.IsSuccess)
		{
			return new ObservedTarget(status, facts, null);
		}

		return status.Kind is ProcessOperationStatusKind.ProtectedLuaFailure or ProcessOperationStatusKind.InvalidResult
			? ObserveNarrowly(port, status)
			: new ObservedTarget(status, default, null);
	}

	private static ObservedTarget ObserveNarrowly(ITargetObservationPort port, ProcessOperationStatus fullStatus)
	{
		ProcessOperationStatus opening = port.ObserveCurrent(out CurrentProcessObservation selected);
		if (!opening.IsSuccess)
		{
			return new ObservedTarget(opening, default, fullStatus);
		}

		ProcessOperationStatus configured = port.TryGetConfiguredPointerSize(out int rawBytes, out _);
		ProcessOperationStatus closing = port.ObserveCurrent(out CurrentProcessObservation confirmed);
		ProcessOperationStatus confirmation = closing.Kind switch
		{
			ProcessOperationStatusKind.Success when confirmed.Id == selected.Id => ProcessOperationStatus.Success,
			ProcessOperationStatusKind.Success or ProcessOperationStatusKind.TargetNotAttached
				or ProcessOperationStatusKind.FileAsProcessTarget => ProcessOperationStatus.TargetChanged,
			_ => closing
		};
		if (!confirmation.IsSuccess)
		{
			return new ObservedTarget(confirmation, default, fullStatus);
		}

		// TryGetConfiguredPointerSize keeps the raw integer of an InvalidResult width (any value other than 4 and 8);
		// zero there means that no integer was read, so it stays unknown.
		int? configuredBytes = configured.IsSuccess ||
							   (configured.Kind == ProcessOperationStatusKind.InvalidResult && rawBytes != 0)
			? rawBytes
			: null;
		TargetArchitectureObservation narrowed = new(selected.Id, TargetBackend.Unknown, selected.PointerSize, null,
			null, null, null, configuredBytes);
		return new ObservedTarget(ProcessOperationStatus.Success, narrowed, fullStatus);
	}
}

/// <summary>The copied result of one target observation.</summary>
/// <param name="Status">
///     Successful when target facts were established (fully or narrowed); otherwise the SDK status that explains why no
///     fact is attributed to a target.
/// </param>
/// <param name="Facts">The copied facts; meaningful only when <see cref="HasTarget" /> is <see langword="true" />.</param>
/// <param name="NarrowedFrom">
///     The status of the full observation when the observer had to narrow, or <see langword="null" /> when the full
///     observation answered.
/// </param>
internal readonly record struct ObservedTarget(
	ProcessOperationStatus Status,
	TargetArchitectureObservation Facts,
	ProcessOperationStatus? NarrowedFrom)
{
	/// <summary>Gets whether target facts were established for one selected process.</summary>
	internal bool HasTarget => Status.IsSuccess;

	/// <summary>Gets whether Cheat Engine reported that no target is selected.</summary>
	internal bool NoTargetSelected => Status.Kind == ProcessOperationStatusKind.TargetNotAttached;

	/// <summary>Gets the selected process identifier, or <see langword="null" /> without a target.</summary>
	internal TargetProcessId? ProcessId => HasTarget ? Facts.ProcessId : null;

	/// <summary>
	///     Gets how Cheat Engine reaches the target: the SDK's backend fact, <see cref="TargetBackend.FileAsProcess" />
	///     for the file-as-process sentinel, otherwise unknown.
	/// </summary>
	internal TargetBackend Backend => HasTarget
		? Facts.Backend
		: Status.Kind == ProcessOperationStatusKind.FileAsProcessTarget
			? TargetBackend.FileAsProcess
			: TargetBackend.Unknown;

	/// <summary>Gets the target bitness (<c>targetIs64Bit</c>, what <c>readPointer</c> follows), or unknown.</summary>
	internal PointerSize Bitness => HasTarget ? Facts.Bitness : PointerSize.Unknown;

	/// <summary>Gets the ISA the SDK derived from the family and bitness facts, or unknown.</summary>
	internal CheatEngineArchitecture Architecture => HasTarget ? Facts.Architecture : CheatEngineArchitecture.Unknown;

	/// <summary>Gets the decoded target ABI, or unknown.</summary>
	internal TargetAbi Abi => HasTarget ? Facts.Abi : TargetAbi.Unknown;

	/// <summary>Gets the raw configured pointer size, or <see langword="null" /> when it was not observed.</summary>
	internal int? ConfiguredPointerSizeBytes => HasTarget ? Facts.ConfiguredPointerSizeBytes : null;

	/// <summary>Gets the configured pointer size as a width when it is 4 or 8 bytes, otherwise unknown.</summary>
	internal PointerSize ConfiguredPointerSize => HasTarget ? Facts.ConfiguredPointerSize : PointerSize.Unknown;

	/// <summary>
	///     Gets whether an observed configured pointer size differs from the known bitness (audit Q31.a);
	///     <see langword="false" /> when either fact is unknown, which is no evidence of a mismatch.
	/// </summary>
	internal bool ConfiguredPointerSizeDiffersFromBitness =>
		HasTarget && Facts.ConfiguredPointerSizeDiffersFromBitness == true;
}
