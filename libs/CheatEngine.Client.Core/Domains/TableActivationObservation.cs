using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>The copied facts of one <c>AddressListMutations.SetActive</c> command and the record it left.</summary>
/// <remarks>
///     CheatEngine.SDK reads the record's <c>Active</c> state before the change, calls the setter at most once and never
///     retries, then reads <c>Active</c> and <c>AsyncProcessing</c> back; <see cref="Kind" /> is its classification. The
///     snapshot is copied afterwards, in the same dispatched callback, and is never merged with the command result.
/// </remarks>
/// <param name="Kind">The activation outcome CheatEngine.SDK reported.</param>
/// <param name="Problem">
///     Why the command was not attempted, or the problem of an indeterminate command;
///     <see cref="MemoryRecordMutationProblem.None" /> otherwise.
/// </param>
/// <param name="Snapshot">
///     The record copied after the command, when the setter ran or was not needed and the copy succeeded.
/// </param>
internal readonly record struct TableActivationObservation(
	MemoryRecordActivationOutcomeKind Kind,
	MemoryRecordMutationProblem Problem,
	MemoryRecordSnapshot? Snapshot)
{
	/// <summary>Creates an observation without a record snapshot.</summary>
	/// <param name="kind">The activation outcome.</param>
	/// <param name="problem">The problem CheatEngine.SDK reported with it.</param>
	/// <returns>The observation.</returns>
	internal static TableActivationObservation Of(MemoryRecordActivationOutcomeKind kind,
		MemoryRecordMutationProblem problem = MemoryRecordMutationProblem.None)
	{
		return new TableActivationObservation(kind, problem, null);
	}
}
