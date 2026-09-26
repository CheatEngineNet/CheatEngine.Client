using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>The copied facts of one Address List record mutation, in CheatEngine.SDK's mutation vocabulary.</summary>
/// <remarks>
///     <para>
///         <c>AddressListMutations.Delete</c> and <c>AddressListMutations.SetParent</c> report these two facts
///         themselves. The Client's own create and select steps, for which CheatEngine.SDK 2.0.0 has no
///         <c>AddressListMutations</c> command, report theirs in the same vocabulary, so <see cref="TableMapping" />
///         classifies every record mutation once.
///     </para>
///     <para>
///         The default value is <see cref="MemoryRecordMutationProblem.Uninitialized" /> and never reads as a success.
///     </para>
/// </remarks>
/// <param name="Effect">How far the mutation progressed at the Cheat Engine boundary.</param>
/// <param name="Problem">The problem, or <see cref="MemoryRecordMutationProblem.None" /> for a success.</param>
internal readonly record struct TableRecordMutationOutcome(
	MemoryRecordMutationEffect Effect,
	MemoryRecordMutationProblem Problem)
{
	/// <summary>Gets the outcome of a mutation that completed with no problem.</summary>
	internal static TableRecordMutationOutcome Succeeded =>
		new(MemoryRecordMutationEffect.Completed, MemoryRecordMutationProblem.None);

	/// <summary>
	///     Gets the outcome of a mutation that completed when the record could not be copied afterwards. CheatEngine.SDK
	///     asks callers never to merge such a read failure with the command result, so it is its own invalid result
	///     after a completed command.
	/// </summary>
	internal static TableRecordMutationOutcome CompletedWithoutSnapshot =>
		new(MemoryRecordMutationEffect.Completed, MemoryRecordMutationProblem.InvalidResult);

	/// <summary>
	///     Gets the outcome of a Cheat Engine call that was invoked and returned no usable result, so whether it changed
	///     the Address List is not established.
	/// </summary>
	internal static TableRecordMutationOutcome InvalidResultAfterInvocation =>
		new(MemoryRecordMutationEffect.Indeterminate, MemoryRecordMutationProblem.InvalidResult);

	/// <summary>Gets whether the mutation completed with no problem.</summary>
	internal bool IsSuccess =>
		Effect == MemoryRecordMutationEffect.Completed && Problem == MemoryRecordMutationProblem.None;

	/// <summary>Creates the outcome of a mutation refused before any Cheat Engine change was attempted.</summary>
	/// <param name="problem">Why the mutation was not attempted.</param>
	/// <returns>A <see cref="MemoryRecordMutationEffect.NotAttempted" /> outcome.</returns>
	internal static TableRecordMutationOutcome NotAttempted(MemoryRecordMutationProblem problem)
	{
		return new TableRecordMutationOutcome(MemoryRecordMutationEffect.NotAttempted, problem);
	}

	/// <summary>Copies the facts of an <c>AddressListMutations</c> command.</summary>
	/// <param name="outcome">The command result CheatEngine.SDK returned.</param>
	/// <returns>The copied effect and problem.</returns>
	internal static TableRecordMutationOutcome From(MemoryRecordMutationOutcome outcome)
	{
		return new TableRecordMutationOutcome(outcome.Effect, outcome.Problem);
	}
}
