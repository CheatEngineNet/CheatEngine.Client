using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Resolves the root of a hierarchy copy, so that <see cref="TableClient" />'s walk of each record's children by
///     position is testable without Cheat Engine. Every member is read-only.
/// </summary>
internal interface ITableHierarchyPort
{
	/// <summary>Resolves the record that roots a hierarchy copy in the current Address List.</summary>
	/// <param name="id">The identifier of the root record.</param>
	/// <param name="root">The root record when the result is <see cref="RecordLookupStatus.Success" />.</param>
	/// <returns>
	///     <see cref="RecordLookupStatus.Success" />, <see cref="RecordLookupStatus.NotFound" /> or
	///     <see cref="RecordLookupStatus.AddressListUnavailable" />.
	/// </returns>
	public RecordLookupStatus TryGetRoot(MemoryRecordId id, out ITableHierarchyRecord? root);
}

/// <summary>One record reached by a hierarchy copy: its snapshot and its immediate children, by position.</summary>
internal interface ITableHierarchyRecord
{
	/// <summary>Copies the record, with its reported child count in its state.</summary>
	/// <param name="snapshot">The copied record on success.</param>
	/// <returns><see langword="true" /> when every required field was read.</returns>
	public bool TrySnapshot(out MemoryRecordSnapshot snapshot);

	/// <summary>Gets the immediate child at <paramref name="index" />.</summary>
	/// <param name="index">The zero-based position of the child.</param>
	/// <param name="child">The child on success.</param>
	/// <returns>
	///     <see langword="true" /> when Cheat Engine returned a child at that position; a position past the last child and
	///     a failed read both return <see langword="false" />.
	/// </returns>
	public bool TryGetChild(int index, [NotNullWhen(true)] out ITableHierarchyRecord? child);
}
