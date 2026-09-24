using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Tables;

/// <summary>A copied, handle-free snapshot of a Cheat Engine memory record.</summary>
/// <remarks>
///     The record's fields are grouped: <see cref="Content" /> holds what defines the record and <see cref="State" />
///     holds its runtime state. Nothing is copied twice.
/// </remarks>
public readonly record struct MemoryRecordSnapshot
{
	/// <summary>Creates a copied memory-record snapshot from grouped content and state fields.</summary>
	/// <param name="id">The stable Cheat Engine record identifier.</param>
	/// <param name="index">The current zero-based address-list position.</param>
	/// <param name="content">The copied content fields.</param>
	/// <param name="state">The copied runtime state fields.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is negative.</exception>
	/// <exception cref="ArgumentException"><paramref name="content" /> is uninitialized.</exception>
	public MemoryRecordSnapshot(
		MemoryRecordId id,
		int index,
		MemoryRecordContentSnapshot content,
		MemoryRecordStateSnapshot state)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		if (content.Description is null || content.AddressExpression is null || content.Value is null)
		{
			throw new ArgumentException("Content requires non-null text fields.", nameof(content));
		}

		Id = id;
		Index = index;
		Content = content;
		State = state;
	}

	/// <summary>Gets the stable Cheat Engine record identifier.</summary>
	public MemoryRecordId Id
	{
		get;
	}

	/// <summary>Gets the current zero-based address-list position.</summary>
	public int Index
	{
		get;
	}

	/// <summary>Gets the copied content fields for this memory record.</summary>
	public MemoryRecordContentSnapshot Content
	{
		get;
	}

	/// <summary>Gets the copied runtime state fields for this memory record.</summary>
	public MemoryRecordStateSnapshot State
	{
		get;
	}
}
