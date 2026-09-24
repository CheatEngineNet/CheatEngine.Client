using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Maps every outcome that CheatEngine.SDK 2.0.0 reports for Address List record mutations
///     (<c>AddressListMutations.Delete</c>, <c>SetParent</c> and <c>SetActive</c>) to the Client vocabulary, value by
///     value.
/// </summary>
/// <remarks>
///     <para>A record mutation maps its <see cref="MemoryRecordMutationProblem" /> to a failure kind:</para>
///     <list type="table">
///         <listheader>
///             <term>SDK problem</term>
///             <description>Failure kind</description>
///         </listheader>
///         <item><term><c>TableLoadInProgress</c></term><description><c>InvalidState</c></description></item>
///         <item><term><c>RuntimeIdentityChanged</c></term><description><c>RuntimeChanged</c></description></item>
///         <item>
///             <term><c>CycleDetected</c>, <c>SelfParent</c></term>
///             <description><c>OperationRejected</c></description>
///         </item>
///         <item><term><c>TraversalLimitReached</c></term><description><c>ResultLimitExceeded</c></description></item>
///         <item>
///             <term><c>ParentNotFound</c>, <c>RecordNotFound</c></term>
///             <description><c>NotFound</c></description>
///         </item>
///         <item><term><c>LuaFailure</c></term><description><c>LuaError</c></description></item>
///         <item><term><c>InvalidResult</c></term><description><c>InvalidHostResult</c></description></item>
///         <item>
///             <term><c>AddressListUnavailable</c>, <c>GlobalUnavailable</c></term>
///             <description><c>CapabilityUnavailable</c></description>
///         </item>
///         <item>
///             <term>
///                 <c>Uninitialized</c>, <c>None</c> on a mutation that did not complete, or an undefined problem
///             </term>
///             <description><c>IndeterminateHostResult</c>, with an <c>Unknown</c> host effect</description>
///         </item>
///     </list>
///     <para>
///         and its <see cref="MemoryRecordMutationEffect" /> to the host effect: <c>NotAttempted</c> is
///         <c>NotStarted</c>, <c>Completed</c> is <c>Completed</c> (the command completed and the record could not be
///         copied afterwards), <c>Indeterminate</c> is <c>Started</c> (the command began and raised, so part of it may
///         persist; it is never retried), and an undefined effect is <c>Unknown</c>.
///     </para>
///     <para>An activation maps its <see cref="MemoryRecordActivationOutcomeKind" />:</para>
///     <list type="table">
///         <listheader>
///             <term>SDK kind</term>
///             <description>Result</description>
///         </listheader>
///         <item>
///             <term><c>Applied</c>, <c>Unchanged</c></term>
///             <description>Success, with the record copied after the command</description>
///         </item>
///         <item>
///             <term><c>Pending</c></term>
///             <description>
///                 <c>IndeterminateHostResult</c>, <c>Started</c>: the record activates asynchronously and its final state
///                 is not observable in the call
///             </description>
///         </item>
///         <item>
///             <term><c>RefusedByHost</c></term>
///             <description>
///                 <c>OperationRejected</c>, <c>Started</c>: the setter ran and the record reads back its previous state;
///                 CheatEngine.SDK states that the refusal may have applied part of its effects
///             </description>
///         </item>
///         <item>
///             <term><c>Indeterminate</c></term>
///             <description>
///                 <c>IndeterminateHostResult</c>, <c>Started</c>: the setter ran and its effect is unknown
///             </description>
///         </item>
///         <item>
///             <term><c>NotAttempted</c></term>
///             <description>The kind of its problem (table above), <c>NotStarted</c></description>
///         </item>
///         <item>
///             <term><c>Unknown</c> or an undefined kind</term>
///             <description><c>IndeterminateHostResult</c>, <c>Unknown</c></description>
///         </item>
///     </list>
///     <para>
///         Messages name the category only, never a record description, an address or a path. The mapping-totality
///         tests fail when the consumed SDK adds a value.
///     </para>
/// </remarks>
internal static class TableMapping
{
	/// <summary>The message of an unavailable Address List (a capability condition, ADR-08).</summary>
	internal const string AddressListUnavailableMessage = "Cheat Engine's Address List capability is unavailable.";

	/// <summary>The message of an absent record.</summary>
	internal const string RecordNotFoundMessage = "The requested Cheat Engine memory record was not found.";

	/// <summary>The message of a Cheat Engine result outside the typed Address List contract.</summary>
	internal const string InvalidContractMessage = "Cheat Engine did not return the expected Address List contract.";

	/// <summary>Returns the failure kind of a record mutation that did not succeed.</summary>
	/// <param name="problem">The problem CheatEngine.SDK or the Client step reported.</param>
	/// <returns>
	///     The failure kind; <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> for an unrecognized value.
	/// </returns>
	internal static CheatEngineFailureKind ToFailureKind(MemoryRecordMutationProblem problem)
	{
		return problem switch
		{
			MemoryRecordMutationProblem.TableLoadInProgress => CheatEngineFailureKind.InvalidState,
			MemoryRecordMutationProblem.RuntimeIdentityChanged => CheatEngineFailureKind.RuntimeChanged,
			MemoryRecordMutationProblem.CycleDetected => CheatEngineFailureKind.OperationRejected,
			MemoryRecordMutationProblem.SelfParent => CheatEngineFailureKind.OperationRejected,
			MemoryRecordMutationProblem.TraversalLimitReached => CheatEngineFailureKind.ResultLimitExceeded,
			MemoryRecordMutationProblem.ParentNotFound => CheatEngineFailureKind.NotFound,
			MemoryRecordMutationProblem.RecordNotFound => CheatEngineFailureKind.NotFound,
			MemoryRecordMutationProblem.LuaFailure => CheatEngineFailureKind.LuaError,
			MemoryRecordMutationProblem.InvalidResult => CheatEngineFailureKind.InvalidHostResult,
			MemoryRecordMutationProblem.AddressListUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			MemoryRecordMutationProblem.GlobalUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			MemoryRecordMutationProblem.None => CheatEngineFailureKind.IndeterminateHostResult,
			MemoryRecordMutationProblem.Uninitialized => CheatEngineFailureKind.IndeterminateHostResult,
			_ => CheatEngineFailureKind.IndeterminateHostResult
		};
	}

	/// <summary>Returns the host effect of a record mutation that did not succeed.</summary>
	/// <param name="effect">How far the mutation progressed.</param>
	/// <returns>The host effect; <see cref="CheatEngineHostEffect.Unknown" /> for an unrecognized value.</returns>
	internal static CheatEngineHostEffect ToHostEffect(MemoryRecordMutationEffect effect)
	{
		return effect switch
		{
			MemoryRecordMutationEffect.NotAttempted => CheatEngineHostEffect.NotStarted,
			MemoryRecordMutationEffect.Completed => CheatEngineHostEffect.Completed,
			MemoryRecordMutationEffect.Indeterminate => CheatEngineHostEffect.Started,
			_ => CheatEngineHostEffect.Unknown
		};
	}

	/// <summary>Creates the failure of a record mutation that did not succeed.</summary>
	/// <param name="operation">The public operation name.</param>
	/// <param name="outcome">The mutation outcome.</param>
	/// <returns>
	///     The failure. An unrecognized problem is <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> with an
	///     <see cref="CheatEngineHostEffect.Unknown" /> effect, never an established one.
	/// </returns>
	internal static CheatEngineFailure MutationFailure(string operation, TableRecordMutationOutcome outcome)
	{
		CheatEngineFailureKind kind = ToFailureKind(outcome.Problem);
		CheatEngineHostEffect effect = kind == CheatEngineFailureKind.IndeterminateHostResult
			? CheatEngineHostEffect.Unknown
			: ToHostEffect(outcome.Effect);
		return new CheatEngineFailure(kind, operation, Describe(outcome), null, effect);
	}

	/// <summary>Returns the failure kind and host effect of an activation, or <see langword="null" /> for a success.</summary>
	/// <param name="kind">The activation outcome CheatEngine.SDK reported.</param>
	/// <param name="problem">The problem CheatEngine.SDK reported with it.</param>
	/// <returns>
	///     <see langword="null" /> for <see cref="MemoryRecordActivationOutcomeKind.Applied" /> and
	///     <see cref="MemoryRecordActivationOutcomeKind.Unchanged" />; otherwise the failure kind and host effect, and
	///     <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> with <see cref="CheatEngineHostEffect.Unknown" />
	///     for an unrecognized kind.
	/// </returns>
	internal static (CheatEngineFailureKind Kind, CheatEngineHostEffect HostEffect)? ToActivationFailure(
		MemoryRecordActivationOutcomeKind kind, MemoryRecordMutationProblem problem)
	{
		return kind switch
		{
			MemoryRecordActivationOutcomeKind.Applied => null,
			MemoryRecordActivationOutcomeKind.Unchanged => null,
			MemoryRecordActivationOutcomeKind.Pending =>
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Started),
			MemoryRecordActivationOutcomeKind.RefusedByHost =>
				(CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.Started),
			MemoryRecordActivationOutcomeKind.Indeterminate =>
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Started),
			MemoryRecordActivationOutcomeKind.NotAttempted => (ToFailureKind(problem), CheatEngineHostEffect.NotStarted),
			MemoryRecordActivationOutcomeKind.Unknown =>
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown),
			_ => (CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown)
		};
	}

	/// <summary>Gets whether the port copies the record after an activation of this kind.</summary>
	/// <param name="kind">The activation outcome.</param>
	/// <returns>
	///     <see langword="true" /> when the setter ran or was not needed and its outcome is known: the record then has a
	///     state worth returning.
	/// </returns>
	internal static bool CopiesRecord(MemoryRecordActivationOutcomeKind kind)
	{
		return kind is MemoryRecordActivationOutcomeKind.Applied or MemoryRecordActivationOutcomeKind.Unchanged
			or MemoryRecordActivationOutcomeKind.Pending or MemoryRecordActivationOutcomeKind.RefusedByHost;
	}

	/// <summary>Classifies one activation observation.</summary>
	/// <param name="operation">The public operation name.</param>
	/// <param name="requested">The requested <c>Active</c> state.</param>
	/// <param name="observation">The copied command facts and record.</param>
	/// <param name="failure">The failure when the activation is not a success.</param>
	/// <returns>
	///     <see langword="true" /> for an applied or unchanged activation whose record was copied; a success whose record
	///     could not be copied is <see cref="CheatEngineFailureKind.InvalidHostResult" />, never merged with the command.
	/// </returns>
	internal static bool TryClassifyActivation(string operation, bool requested, TableActivationObservation observation,
		out CheatEngineFailure failure)
	{
		if (ToActivationFailure(observation.Kind, observation.Problem) is not { } classified)
		{
			if (observation.Snapshot is not null)
			{
				failure = default;
				return true;
			}

			failure = observation.Kind == MemoryRecordActivationOutcomeKind.Unchanged
				? new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
					"The memory record already had the requested state, but its snapshot could not be copied.", null,
					CheatEngineHostEffect.NotStarted)
				: new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
					"Cheat Engine applied the requested state, but the record snapshot could not be copied.", null,
					CheatEngineHostEffect.Completed);
			return false;
		}

		string message = observation.Kind switch
		{
			MemoryRecordActivationOutcomeKind.RefusedByHost =>
				$"Cheat Engine left the memory record {(requested ? "inactive" : "active")}; an activation callback, " +
				"script or record type refused the change; partial script effects may persist.",
			MemoryRecordActivationOutcomeKind.Pending =>
				"The record activates asynchronously; its final state is not observable in this call.",
			MemoryRecordActivationOutcomeKind.Indeterminate =>
				"Cheat Engine ran the activation setter, but the record's state after it could not be established; " +
				"its effect is unknown.",
			MemoryRecordActivationOutcomeKind.NotAttempted =>
				Describe(TableRecordMutationOutcome.NotAttempted(observation.Problem)),
			_ => "CheatEngine.SDK reported no recognized activation outcome."
		};
		failure = new CheatEngineFailure(classified.Kind, operation, message, null, classified.HostEffect);
		return false;
	}

	private static string Describe(TableRecordMutationOutcome outcome)
	{
		return outcome.Problem switch
		{
			MemoryRecordMutationProblem.AddressListUnavailable => AddressListUnavailableMessage,
			MemoryRecordMutationProblem.GlobalUnavailable => "A Cheat Engine Address List function is unavailable.",
			MemoryRecordMutationProblem.RecordNotFound => RecordNotFoundMessage,
			MemoryRecordMutationProblem.ParentNotFound =>
				"The requested parent Cheat Engine memory record was not found.",
			MemoryRecordMutationProblem.SelfParent => "A memory record cannot be its own parent.",
			MemoryRecordMutationProblem.CycleDetected =>
				"The requested parent relationship would form a cycle in the Address List.",
			MemoryRecordMutationProblem.TraversalLimitReached =>
				"The parent chain of the requested parent reaches the traversal limit of " +
				$"{SdkTableRecordMutationPort.ParentTraversalHops} records; the record was not moved.",
			MemoryRecordMutationProblem.LuaFailure when outcome.Effect == MemoryRecordMutationEffect.Indeterminate =>
				"A Cheat Engine Address List call raised after the change started; its effect is unknown.",
			MemoryRecordMutationProblem.LuaFailure =>
				"A Cheat Engine Address List call raised before the change was attempted.",
			MemoryRecordMutationProblem.InvalidResult when outcome.Effect == MemoryRecordMutationEffect.Completed =>
				"Cheat Engine completed the change, but the memory record could not be copied afterwards.",
			MemoryRecordMutationProblem.InvalidResult => InvalidContractMessage,
			MemoryRecordMutationProblem.TableLoadInProgress =>
				"A table file is loading on Cheat Engine's main thread (a script of that table called the Client); " +
				"the memory record was not changed.",
			MemoryRecordMutationProblem.RuntimeIdentityChanged =>
				"Cheat Engine's Lua runtime changed before the change was attempted; the memory record was not changed.",
			_ => "CheatEngine.SDK reported no recognized Address List mutation outcome."
		};
	}
}
