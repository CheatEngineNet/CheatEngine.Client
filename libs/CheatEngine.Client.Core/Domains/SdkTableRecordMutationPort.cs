using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Protected SDK implementation of record creation, deletion, selection, activation and parent reassignment. Delete,
///     parent assignment and activation are CheatEngine.SDK's <see cref="AddressListMutations" /> commands, which resolve
///     the record by identifier in the current list, refuse while a table file loads on the calling thread or after the
///     Lua runtime changed, and report how far they got; the port adds no Lua of its own.
/// </summary>
internal sealed class SdkTableRecordMutationPort : ITableRecordMutationPort
{
	/// <summary>
	///     The explicit bound of the parent-chain walk that <c>AddressListMutations.SetParent</c> runs before it assigns
	///     a parent; the Client never relies on the SDK's default bound.
	/// </summary>
	/// <remarks>
	///     Cheat Engine tables are shallow; a longer chain above the requested parent is refused as
	///     <see cref="MemoryRecordMutationProblem.TraversalLimitReached" /> instead of being walked without bound.
	/// </remarks>
	internal const int ParentTraversalHops = 4096;

	private static readonly MemoryRecordParentTraversalLimit ParentTraversalLimit = new(ParentTraversalHops);

	/// <inheritdoc />
	/// <remarks>
	///     When initialization, snapshotting or the parent assignment fails, the record created by this call is deleted
	///     exactly once through <see cref="AddressListMutations.Delete" /> with its identifier. Only a completed delete is
	///     <see cref="TableRecordRollback.Confirmed" />; any other result, a fault, or an identifier that could not be
	///     read is <see cref="TableRecordRollback.Unconfirmed" />, never retried (audit A08-14).
	/// </remarks>
	public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record)
	{
		record = default;
		if (!AddressListAccess.TryGetCurrent(out AddressList list))
		{
			return new TableRecordCreation(
				TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.AddressListUnavailable),
				TableRecordRollback.NotRequired);
		}

		if (!list.TryCreateMemoryRecord(out MemoryRecord value))
		{
			return new TableRecordCreation(TableRecordMutationOutcome.InvalidResultAfterInvocation,
				TableRecordRollback.NotRequired);
		}

		if (!value.TryGetId(out MemoryRecordId createdId))
		{
			// Without its identifier the created record cannot be deleted through AddressListMutations.
			return new TableRecordCreation(TableRecordMutationOutcome.InvalidResultAfterInvocation,
				TableRecordRollback.Unconfirmed);
		}

		TableRecordMutationOutcome outcome;
		Exception? fault = null;
		try
		{
			outcome = TryInitializeRecord(value, definition)
				? TryCompleteRecordCreation(value, createdId, definition, out record)
				: TableRecordMutationOutcome.InvalidResultAfterInvocation;
			if (outcome.IsSuccess)
			{
				return TableRecordCreation.Created;
			}
		}
		catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
		{
			outcome = TableRecordMutationOutcome.InvalidResultAfterInvocation;
			fault = exception;
		}

		record = default;
		(TableRecordRollback rollback, Exception? rollbackFault) = RollBackCreatedRecord(createdId);
		return new TableRecordCreation(outcome, rollback, fault, rollbackFault);
	}

	public TableRecordMutationOutcome TryDelete(MemoryRecordId id)
	{
		return TableRecordMutationOutcome.From(AddressListMutations.Delete(id));
	}

	public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
	{
		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(id, requested);
		MemoryRecordSnapshot? snapshot = TableMapping.CopiesRecord(outcome.Kind) &&
										 TryCopyRecord(id, out MemoryRecordSnapshot copied)
			? copied
			: null;
		return new TableActivationObservation(outcome.Kind, outcome.Problem, snapshot);
	}

	public TableRecordMutationOutcome TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
	{
		record = default;
		if (!AddressListAccess.TryGetCurrent(out AddressList list))
		{
			return TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.AddressListUnavailable);
		}

		if (!list.TryGetMemoryRecordById(id, out MemoryRecord value))
		{
			return TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.RecordNotFound);
		}

		if (!list.TrySetSelectedRecord(value))
		{
			return TableRecordMutationOutcome.InvalidResultAfterInvocation;
		}

		return TableClient.TrySnapshot(value, out record)
			? TableRecordMutationOutcome.Succeeded
			: TableRecordMutationOutcome.CompletedWithoutSnapshot;
	}

	public TableRecordMutationOutcome TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
		out MemoryRecordSnapshot record)
	{
		record = default;
		TableRecordMutationOutcome outcome =
			TableRecordMutationOutcome.From(AddressListMutations.SetParent(childId, parentId, ParentTraversalLimit));
		if (!outcome.IsSuccess)
		{
			return outcome;
		}

		return TryCopyRecord(childId, out record) ? outcome : TableRecordMutationOutcome.CompletedWithoutSnapshot;
	}

	private static bool TryInitializeRecord(MemoryRecord value, MemoryRecordDefinition definition)
	{
		return value.TrySetDescription(definition.Description) &&
			   value.TrySetAddressExpression(definition.AddressExpression) &&
			   value.TrySetVariableType(definition.VariableType) &&
			   value.TrySetValue(definition.Value);
	}

	private TableRecordMutationOutcome TryCompleteRecordCreation(MemoryRecord value, MemoryRecordId createdId,
		MemoryRecordDefinition definition, out MemoryRecordSnapshot record)
	{
		if (definition.ParentId is { } parentId)
		{
			return TrySetParent(createdId, parentId, out record);
		}

		return TableClient.TrySnapshot(value, out record)
			? TableRecordMutationOutcome.Succeeded
			: TableRecordMutationOutcome.CompletedWithoutSnapshot;
	}

	/// <summary>Deletes the record created by this call exactly once and reports whether Cheat Engine confirmed it.</summary>
	private static (TableRecordRollback Rollback, Exception? Fault) RollBackCreatedRecord(MemoryRecordId createdId)
	{
		try
		{
			return (AddressListMutations.Delete(createdId).IsCompleted
				? TableRecordRollback.Confirmed
				: TableRecordRollback.Unconfirmed, null);
		}
		catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
		{
			return (TableRecordRollback.Unconfirmed, exception);
		}
	}

	/// <summary>Copies the current state of one record after a completed command, never merged with the command.</summary>
	private static bool TryCopyRecord(MemoryRecordId id, out MemoryRecordSnapshot record)
	{
		record = default;
		return AddressListAccess.TryGetCurrent(out AddressList list) &&
			   list.TryGetMemoryRecordById(id, out MemoryRecord value) &&
			   TableClient.TrySnapshot(value, out record);
	}
}
