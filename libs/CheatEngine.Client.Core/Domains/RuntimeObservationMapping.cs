using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Maps the statuses of the CheatEngine.SDK 2.0.0 runtime and process operations to the Client vocabulary, value by
///     value.
/// </summary>
/// <remarks>
///     <para>
///         Every value of <see cref="ProcessOperationStatusKind" />, <see cref="LuaOperationStatusKind" /> and
///         <see cref="RuntimeCapabilityAvailabilityState" /> has a deliberate Client counterpart. A value this Client
///         version does not know takes the conservative fallback (an indeterminate failure, or unknown evidence), never
///         an established outcome; the mapping-totality tests fail when the consumed SDK adds a value.
///     </para>
///     <para>
///         Process-operation failures:
///     </para>
///     <list type="table">
///         <listheader>
///             <term>SDK status</term>
///             <description>Client failure kind</description>
///         </listheader>
///         <item><term><c>TargetNotAttached</c></term><description><c>TargetNotAttached</c></description></item>
///         <item><term><c>SelectionNotConfirmed</c></term><description><c>OperationRejected</c></description></item>
///         <item><term><c>GlobalUnavailable</c></term><description><c>CapabilityUnavailable</c></description></item>
///         <item><term><c>ProtectedLuaFailure</c></term><description><c>LuaError</c></description></item>
///         <item><term><c>InvalidResult</c></term><description><c>InvalidHostResult</c></description></item>
///         <item><term><c>TargetChanged</c></term><description><c>TargetChanged</c></description></item>
///         <item><term><c>FileAsProcessTarget</c></term><description><c>TargetIdentityUnavailable</c></description></item>
///         <item><term><c>Unknown</c> or undefined</term><description><c>IndeterminateHostResult</c></description></item>
///     </list>
///     <para>
///         <c>Success</c> is not a failure and maps to <see cref="CheatEngineFailureKind.Unknown" />, which no caller
///         reports. The target-selection statuses (<see cref="TargetSelectionObservationStatus" />,
///         <see cref="TargetIdentityCheckKind" />) map to what they establish about the selected process's identity:
///         a failed or empty observation is never evidence of a change.
///     </para>
/// </remarks>
internal static class RuntimeObservationMapping
{
	/// <summary>Returns the Client failure kind of a non-successful process-operation status.</summary>
	/// <param name="kind">The SDK process-operation status kind.</param>
	/// <returns>The failure kind; <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> for an undefined value.</returns>
	internal static CheatEngineFailureKind ToFailureKind(ProcessOperationStatusKind kind)
	{
		return kind switch
		{
			ProcessOperationStatusKind.Success => CheatEngineFailureKind.Unknown,
			ProcessOperationStatusKind.TargetNotAttached => CheatEngineFailureKind.TargetNotAttached,
			ProcessOperationStatusKind.SelectionNotConfirmed => CheatEngineFailureKind.OperationRejected,
			ProcessOperationStatusKind.GlobalUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			ProcessOperationStatusKind.ProtectedLuaFailure => CheatEngineFailureKind.LuaError,
			ProcessOperationStatusKind.InvalidResult => CheatEngineFailureKind.InvalidHostResult,
			ProcessOperationStatusKind.TargetChanged => CheatEngineFailureKind.TargetChanged,
			ProcessOperationStatusKind.FileAsProcessTarget => CheatEngineFailureKind.TargetIdentityUnavailable,
			ProcessOperationStatusKind.Unknown => CheatEngineFailureKind.IndeterminateHostResult,
			_ => CheatEngineFailureKind.IndeterminateHostResult
		};
	}

	/// <summary>Creates the failure of a non-successful process-operation status.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="status">The SDK status; a successful status is a caller bug.</param>
	/// <param name="hostEffect">What is known about the Cheat Engine side effect.</param>
	/// <returns>The classified failure, with a stable message that names no process, path or address.</returns>
	/// <exception cref="ArgumentException"><paramref name="status" /> is successful.</exception>
	internal static CheatEngineFailure ToFailure(string operation, ProcessOperationStatus status,
		CheatEngineHostEffect hostEffect)
	{
		if (status.IsSuccess)
		{
			throw new ArgumentException("A successful process-operation status is not a failure.", nameof(status));
		}

		return new CheatEngineFailure(ToFailureKind(status.Kind), operation, DescribeFailure(status.Kind), null,
			hostEffect);
	}

	/// <summary>
	///     Returns the host evidence a target observation gives about the selected-process primitive when
	///     CheatEngine.SDK produced no runtime snapshot.
	/// </summary>
	/// <param name="kind">The status of the Client's own target observation.</param>
	/// <returns>
	///     <see cref="ClientCapabilityEvidenceState.Satisfied" /> when the selection was read (a target, no target),
	///     <see cref="ClientCapabilityEvidenceState.Missing" /> for an absent global,
	///     <see cref="ClientCapabilityEvidenceState.Faulted" /> for a raising global,
	///     <see cref="ClientCapabilityEvidenceState.Malformed" /> for a malformed value, and
	///     <see cref="ClientCapabilityEvidenceState.Unknown" /> when the observation cannot be attributed to one target
	///     or has no capability profile (file as process), or for an undefined value.
	/// </returns>
	internal static ClientCapabilityEvidenceState ToHostEvidenceState(ProcessOperationStatusKind kind)
	{
		return kind switch
		{
			ProcessOperationStatusKind.Success => ClientCapabilityEvidenceState.Satisfied,
			ProcessOperationStatusKind.TargetNotAttached => ClientCapabilityEvidenceState.Satisfied,
			ProcessOperationStatusKind.GlobalUnavailable => ClientCapabilityEvidenceState.Missing,
			ProcessOperationStatusKind.ProtectedLuaFailure => ClientCapabilityEvidenceState.Faulted,
			ProcessOperationStatusKind.InvalidResult => ClientCapabilityEvidenceState.Malformed,
			ProcessOperationStatusKind.SelectionNotConfirmed => ClientCapabilityEvidenceState.Unknown,
			ProcessOperationStatusKind.TargetChanged => ClientCapabilityEvidenceState.Unknown,
			ProcessOperationStatusKind.FileAsProcessTarget => ClientCapabilityEvidenceState.Unknown,
			ProcessOperationStatusKind.Unknown => ClientCapabilityEvidenceState.Unknown,
			_ => ClientCapabilityEvidenceState.Unknown
		};
	}

	/// <summary>Returns the evidence of one host fact read on its own.</summary>
	/// <param name="kind">The status of the SDK host operation.</param>
	/// <returns>
	///     <see cref="ClientCapabilityEvidenceState.Satisfied" /> only for a successful read, whose value is kept; every
	///     other status leaves the fact unknown: an absent global is <see cref="ClientCapabilityEvidenceState.Missing" />,
	///     a raising call or an exhausted stack is <see cref="ClientCapabilityEvidenceState.Faulted" />, a value of the
	///     wrong shape is <see cref="ClientCapabilityEvidenceState.Malformed" />, and a raw <c>nil</c> (for the file
	///     version, Cheat Engine returning no version) or an undefined value is
	///     <see cref="ClientCapabilityEvidenceState.Unknown" />.
	/// </returns>
	internal static ClientCapabilityEvidenceState ToFactEvidenceState(LuaOperationStatusKind kind)
	{
		return kind switch
		{
			LuaOperationStatusKind.Success => ClientCapabilityEvidenceState.Satisfied,
			LuaOperationStatusKind.GlobalUnavailable => ClientCapabilityEvidenceState.Missing,
			LuaOperationStatusKind.LuaFailure => ClientCapabilityEvidenceState.Faulted,
			LuaOperationStatusKind.StackUnavailable => ClientCapabilityEvidenceState.Faulted,
			LuaOperationStatusKind.NilResult => ClientCapabilityEvidenceState.Unknown,
			LuaOperationStatusKind.InvalidResult => ClientCapabilityEvidenceState.Malformed,
			LuaOperationStatusKind.MissingResult => ClientCapabilityEvidenceState.Malformed,
			LuaOperationStatusKind.ResultCapacityExceeded => ClientCapabilityEvidenceState.Malformed,
			LuaOperationStatusKind.Unknown => ClientCapabilityEvidenceState.Unknown,
			_ => ClientCapabilityEvidenceState.Unknown
		};
	}

	/// <summary>Returns the host evidence of a capability that CheatEngine.SDK probed in its runtime snapshot.</summary>
	/// <param name="state">The SDK availability of the probed capability.</param>
	/// <returns>
	///     <see cref="ClientCapabilityEvidenceState.Satisfied" /> for an available primitive,
	///     <see cref="ClientCapabilityEvidenceState.Missing" /> for an unavailable one, otherwise
	///     <see cref="ClientCapabilityEvidenceState.Unknown" />.
	/// </returns>
	internal static ClientCapabilityEvidenceState ToHostEvidenceState(RuntimeCapabilityAvailabilityState state)
	{
		return state switch
		{
			RuntimeCapabilityAvailabilityState.Available => ClientCapabilityEvidenceState.Satisfied,
			RuntimeCapabilityAvailabilityState.Unavailable => ClientCapabilityEvidenceState.Missing,
			RuntimeCapabilityAvailabilityState.Unknown => ClientCapabilityEvidenceState.Unknown,
			_ => ClientCapabilityEvidenceState.Unknown
		};
	}

	/// <summary>Returns what a selection observation establishes about the identity of the selected process.</summary>
	/// <param name="status">The SDK selection observation status.</param>
	/// <returns>The identity evidence; <see cref="SelectionIdentity.Unavailable" /> for an undefined value.</returns>
	internal static SelectionIdentity ToSelectionIdentity(TargetSelectionObservationStatus status)
	{
		return status switch
		{
			TargetSelectionObservationStatus.CurrentTargetQualified => SelectionIdentity.Qualified,
			TargetSelectionObservationStatus.CurrentTargetUnqualified => SelectionIdentity.Unqualified,
			TargetSelectionObservationStatus.CurrentTargetRemoteBackend => SelectionIdentity.RemoteBackend,
			TargetSelectionObservationStatus.CurrentTargetBackendUnknown => SelectionIdentity.BackendUnknown,
			TargetSelectionObservationStatus.CurrentTargetFileAsProcess => SelectionIdentity.FileAsProcess,
			TargetSelectionObservationStatus.NoTargetSelected => SelectionIdentity.NoTarget,
			TargetSelectionObservationStatus.GlobalUnavailable => SelectionIdentity.Unavailable,
			TargetSelectionObservationStatus.LuaFailure => SelectionIdentity.Unavailable,
			TargetSelectionObservationStatus.InvalidResult => SelectionIdentity.Unavailable,
			TargetSelectionObservationStatus.Unspecified => SelectionIdentity.Unavailable,
			_ => SelectionIdentity.Unavailable
		};
	}

	/// <summary>Returns what an incarnation check establishes about the selected process.</summary>
	/// <param name="kind">The SDK identity check kind.</param>
	/// <returns>The comparison; <see cref="IncarnationComparison.Unavailable" /> for an undefined value.</returns>
	internal static IncarnationComparison ToIncarnationComparison(TargetIdentityCheckKind kind)
	{
		return kind switch
		{
			TargetIdentityCheckKind.Current => IncarnationComparison.Current,
			TargetIdentityCheckKind.ProcessReused => IncarnationComparison.ProcessReused,
			TargetIdentityCheckKind.TargetChanged => IncarnationComparison.SelectionChanged,
			TargetIdentityCheckKind.NoTargetSelected => IncarnationComparison.SelectionChanged,
			TargetIdentityCheckKind.RemoteBackend => IncarnationComparison.SelectionChanged,
			TargetIdentityCheckKind.FileAsProcess => IncarnationComparison.SelectionChanged,
			TargetIdentityCheckKind.CurrentTargetUnqualified => IncarnationComparison.Unavailable,
			TargetIdentityCheckKind.BackendUnknown => IncarnationComparison.Unavailable,
			TargetIdentityCheckKind.GlobalUnavailable => IncarnationComparison.Unavailable,
			TargetIdentityCheckKind.LuaFailure => IncarnationComparison.Unavailable,
			TargetIdentityCheckKind.InvalidResult => IncarnationComparison.Unavailable,
			TargetIdentityCheckKind.Unspecified => IncarnationComparison.Unavailable,
			_ => IncarnationComparison.Unavailable
		};
	}

	private static string DescribeFailure(ProcessOperationStatusKind kind)
	{
		return kind switch
		{
			ProcessOperationStatusKind.TargetNotAttached => "Cheat Engine has no selected target process.",
			ProcessOperationStatusKind.SelectionNotConfirmed =>
				"Cheat Engine did not confirm the requested process as its selected target.",
			ProcessOperationStatusKind.GlobalUnavailable =>
				"A Cheat Engine global that the target observation requires is not available in this host.",
			ProcessOperationStatusKind.ProtectedLuaFailure =>
				"A Cheat Engine global of the target observation raised a protected Lua error.",
			ProcessOperationStatusKind.InvalidResult =>
				"Cheat Engine returned a target observation outside its documented shape.",
			ProcessOperationStatusKind.TargetChanged =>
				"Cheat Engine's selected target changed while the Client observed it, so the observation cannot be " +
				"attributed to one target; read the current process again.",
			ProcessOperationStatusKind.FileAsProcessTarget =>
				"Cheat Engine's selected target is a file opened as a process: it has no process identity, so no " +
				"target fact is attributed to it.",
			_ => "Cheat Engine reported no result for the target observation."
		};
	}
}

/// <summary>What a selection observation establishes about the identity of the selected process.</summary>
internal enum SelectionIdentity
{
	/// <summary>The observation failed or recorded nothing: no identity evidence, and no evidence of a change.</summary>
	Unavailable = 0,

	/// <summary>A local process with its incarnation (PID and observed creation time).</summary>
	Qualified = 1,

	/// <summary>A local process whose incarnation could not be established.</summary>
	Unqualified = 2,

	/// <summary>A process served by CEServer: a local incarnation does not describe it.</summary>
	RemoteBackend = 3,

	/// <summary>A PID whose backend is not established: no local incarnation can be compared.</summary>
	BackendUnknown = 4,

	/// <summary>No target is selected.</summary>
	NoTarget = 5,

	/// <summary>A file opened as a process is selected; it has no process identity.</summary>
	FileAsProcess = 6
}

/// <summary>What comparing a known incarnation with the current selection establishes.</summary>
internal enum IncarnationComparison
{
	/// <summary>No comparable incarnation was observed: no evidence of a change.</summary>
	Unavailable = 0,

	/// <summary>The selection still denotes the known incarnation.</summary>
	Current = 1,

	/// <summary>The same PID now denotes another process (a different creation time).</summary>
	ProcessReused = 2,

	/// <summary>The selection moved away from the known process (another PID, no target, another backend).</summary>
	SelectionChanged = 3
}
