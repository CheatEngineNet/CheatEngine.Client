using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Tables;

/// <summary>A copied, handle-free snapshot of a Cheat Engine memory record.</summary>
public readonly record struct MemoryRecordSnapshot
{
	/// <summary>Creates a copied memory-record snapshot from grouped content and state fields.</summary>
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

	/// <summary>Gets the record display description.</summary>
	public string Description => Content.Description;

	/// <summary>Gets the record's unresolved Cheat Engine address expression.</summary>
	public string AddressExpression => Content.AddressExpression;

	/// <summary>Gets the record's verbatim value text.</summary>
	public string Value => Content.Value;

	/// <summary>Gets the record's Cheat Engine value type.</summary>
	public VariableType VariableType => Content.VariableType;

	/// <summary>Gets the currently resolved target address when it could be obtained.</summary>
	public Address? CurrentAddress => State.CurrentAddress;

	/// <summary>Gets whether Cheat Engine reports this record as active or frozen.</summary>
	public bool IsActive => State.IsActive;

	/// <summary>Gets the number of immediate child records reported by Cheat Engine.</summary>
	public int ChildCount => State.ChildCount;
}
