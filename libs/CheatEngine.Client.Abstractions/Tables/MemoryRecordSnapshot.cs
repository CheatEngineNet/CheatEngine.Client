using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Tables;

/// <summary>A copied, handle-free snapshot of a Cheat Engine memory record.</summary>
public readonly record struct MemoryRecordSnapshot
{
	/// <summary>Creates a copied memory-record snapshot from grouped content and state fields.</summary>
	public MemoryRecordSnapshot(
		MemoryRecordId id,
		int index,
		MemoryRecordContentSnapshot content,
		MemoryRecordStateSnapshot state)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);

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
	public string Description
	{
		get => Content.Description;
	}

	/// <summary>Gets the record's unresolved Cheat Engine address expression.</summary>
	public string AddressExpression
	{
		get => Content.AddressExpression;
	}

	/// <summary>Gets the record's verbatim value text.</summary>
	public string Value
	{
		get => Content.Value;
	}

	/// <summary>Gets the record's Cheat Engine value type.</summary>
	public VariableType VariableType
	{
		get => Content.VariableType;
	}

	/// <summary>Gets the currently resolved target address when it could be obtained.</summary>
	public Address? CurrentAddress
	{
		get => State.CurrentAddress;
	}

	/// <summary>Gets whether Cheat Engine reports this record as active or frozen.</summary>
	public bool IsActive
	{
		get => State.IsActive;
	}

	/// <summary>Gets the number of immediate child records reported by Cheat Engine.</summary>
	public int ChildCount
	{
		get => State.ChildCount;
	}
}
