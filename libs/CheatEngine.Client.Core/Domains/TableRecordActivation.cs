using CheatEngine.Client.Tables;

namespace CheatEngine.Client.Core.Domains;

/// <summary>What was observed around one request to change a memory record's <c>Active</c> state.</summary>
/// <remarks>The names mirror the SDK 2.0 memory-record activation outcome kinds (docs/migration/sdk-2.0.md).</remarks>
internal enum TableActivationStatus
{
	/// <summary>No outcome was recorded.</summary>
	Unknown = 0,

	/// <summary>Cheat Engine's Address List is not available; nothing was attempted.</summary>
	AddressListUnavailable,

	/// <summary>No record has the requested identifier; nothing was attempted.</summary>
	RecordNotFound,

	/// <summary>The current <c>Active</c> state could not be read, so the setter was not called.</summary>
	NotAttempted,

	/// <summary>The record was already in the requested state; the setter was not called.</summary>
	Unchanged,

	/// <summary>The setter ran once and the record reports the requested state.</summary>
	Applied,

	/// <summary>The setter ran once and the record still reports the other state: Cheat Engine refused the change.</summary>
	RefusedByHost,

	/// <summary>The setter ran once and the record is still processing asynchronously; its final state is unknown.</summary>
	Pending,

	/// <summary>The setter call or the post-change read failed: the effect of the request is unknown.</summary>
	Indeterminate
}

/// <summary>The copied facts of one activation request.</summary>
/// <param name="Status">The classified outcome.</param>
/// <param name="ActiveBefore">The <c>Active</c> state read before the change, when it was read.</param>
/// <param name="ActiveAfter">The <c>Active</c> state read after the change, when it was read.</param>
/// <param name="AsyncProcessingAfter">The <c>AsyncProcessing</c> state read after the change, when it was read.</param>
/// <param name="Snapshot">The copied post-change record snapshot, when it could be copied.</param>
internal readonly record struct TableActivationObservation(
	TableActivationStatus Status,
	bool? ActiveBefore,
	bool? ActiveAfter,
	bool? AsyncProcessingAfter,
	MemoryRecordSnapshot? Snapshot)
{
	internal static TableActivationObservation Of(TableActivationStatus status)
	{
		return new TableActivationObservation(status, null, null, null, null);
	}
}

/// <summary>The four record operations the activation algorithm needs; each returns <see langword="false" /> on failure.</summary>
internal interface IRecordActivationAccess
{
	public bool TryReadActive(out bool active);

	public bool TryWriteActive(bool active);

	public bool TryReadAsyncProcessing(out bool processing);

	public bool TrySnapshot(out MemoryRecordSnapshot snapshot);
}

/// <summary>
///     Changes a memory record's <c>Active</c> state with a real before/after observation instead of a boolean setter
///     (audit ch.14, A14-12, A14-33, A14-34, A14-42, Q35).
/// </summary>
/// <remarks>
///     <para>
///         Read <c>Active</c>; when it already equals the request the setter is not called (<c>Unchanged</c>). Otherwise
///         the setter runs exactly once, then <c>Active</c> and <c>AsyncProcessing</c> are read again: an asynchronous
///         record still processing is <c>Pending</c>, the requested state is <c>Applied</c>, and the other state is
///         <c>RefusedByHost</c> (an activation callback, a script or the record type refused the change). A failed setter
///         call or post-change read is <c>Indeterminate</c>.
///     </para>
///     <para>
///         The request is never retried. An <c>OnActivationFailure</c> handler that asks for a retry already makes
///         Cheat Engine repeat the change inside the single setter call, so a Client retry could loop.
///     </para>
/// </remarks>
internal static class TableRecordActivation
{
	internal static TableActivationObservation Apply<TAccess>(TAccess record, bool requested)
		where TAccess : IRecordActivationAccess
	{
		if (!record.TryReadActive(out bool before))
		{
			return TableActivationObservation.Of(TableActivationStatus.NotAttempted);
		}

		if (before == requested)
		{
			return new TableActivationObservation(TableActivationStatus.Unchanged, before, before, null,
				record.TrySnapshot(out MemoryRecordSnapshot unchanged) ? unchanged : null);
		}

		if (!record.TryWriteActive(requested))
		{
			return new TableActivationObservation(TableActivationStatus.Indeterminate, before, null, null, null);
		}

		if (!record.TryReadActive(out bool after))
		{
			return new TableActivationObservation(TableActivationStatus.Indeterminate, before, null, null, null);
		}

		if (!record.TryReadAsyncProcessing(out bool processing))
		{
			return new TableActivationObservation(TableActivationStatus.Indeterminate, before, after, null, null);
		}

		MemoryRecordSnapshot? snapshot = record.TrySnapshot(out MemoryRecordSnapshot copied) ? copied : null;
		TableActivationStatus status = processing
			? TableActivationStatus.Pending
			: after == requested
				? TableActivationStatus.Applied
				: TableActivationStatus.RefusedByHost;
		return new TableActivationObservation(status, before, after, processing, snapshot);
	}
}
