namespace CheatEngine.Client.Core.Domains;

/// <summary>States whether a failed Address List record creation was rolled back.</summary>
internal enum TableRecordRollback
{
	/// <summary>No record was created, or the creation succeeded: nothing had to be rolled back.</summary>
	NotRequired,

	/// <summary>The partially initialized record was deleted once and Cheat Engine confirmed it.</summary>
	Confirmed,

	/// <summary>
	///     The delete did not complete or faulted, or could not be attempted because the record's identifier was not
	///     readable: the record may remain in the Address List.
	/// </summary>
	Unconfirmed
}

/// <summary>The outcome of creating one Address List record, including the rollback of a failed creation.</summary>
/// <param name="Outcome">The creation or parent-assignment outcome.</param>
/// <param name="Rollback">Whether a partially initialized record was rolled back.</param>
/// <param name="Fault">The SDK fault that interrupted the creation, if any.</param>
/// <param name="RollbackFault">The SDK fault raised by the single rollback attempt, if any.</param>
internal readonly record struct TableRecordCreation(
	TableRecordMutationOutcome Outcome,
	TableRecordRollback Rollback,
	Exception? Fault = null,
	Exception? RollbackFault = null)
{
	internal static TableRecordCreation Created =>
		new(TableRecordMutationOutcome.Succeeded, TableRecordRollback.NotRequired);
}
