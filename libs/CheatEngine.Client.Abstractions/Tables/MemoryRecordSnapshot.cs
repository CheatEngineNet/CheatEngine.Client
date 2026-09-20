using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Tables;

/// <summary>A copied, handle-free snapshot of a Cheat Engine memory record.</summary>
public readonly record struct MemoryRecordSnapshot
{
	/// <summary>Creates a copied memory-record snapshot.</summary>
	public MemoryRecordSnapshot(
		MemoryRecordId id,
		int index,
		string description,
		string addressExpression,
		string value,
		VariableType variableType,
		Address? currentAddress,
		bool isActive = false,
		int childCount = 0)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		ArgumentOutOfRangeException.ThrowIfNegative(childCount);
		ArgumentNullException.ThrowIfNull(description);
		ArgumentNullException.ThrowIfNull(addressExpression);
		ArgumentNullException.ThrowIfNull(value);

		Id = id;
		Index = index;
		Description = description;
		AddressExpression = addressExpression;
		Value = value;
		VariableType = variableType;
		CurrentAddress = currentAddress;
		IsActive = isActive;
		ChildCount = childCount;
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

	/// <summary>Gets the record display description.</summary>
	public string Description
	{
		get;
	}

	/// <summary>Gets the record's unresolved Cheat Engine address expression.</summary>
	public string AddressExpression
	{
		get;
	}

	/// <summary>Gets the record's verbatim value text.</summary>
	public string Value
	{
		get;
	}

	/// <summary>Gets the record's Cheat Engine value type.</summary>
	public VariableType VariableType
	{
		get;
	}

	/// <summary>Gets the currently resolved target address when it could be obtained.</summary>
	public Address? CurrentAddress
	{
		get;
	}

	/// <summary>Gets whether Cheat Engine reports this record as active or frozen.</summary>
	public bool IsActive
	{
		get;
	}

	/// <summary>Gets the number of immediate child records reported by Cheat Engine.</summary>
	public int ChildCount
	{
		get;
	}
}
