using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Tables;

/// <summary>The initial managed fields for a newly created Cheat Engine memory record.</summary>
public readonly record struct MemoryRecordDefinition
{
	/// <summary>Creates a memory-record definition.</summary>
	public MemoryRecordDefinition(string description, string addressExpression, string value, VariableType variableType,
		MemoryRecordId? parentId = null)
	{
		ArgumentNullException.ThrowIfNull(description);
		ArgumentException.ThrowIfNullOrWhiteSpace(addressExpression);
		ArgumentNullException.ThrowIfNull(value);

		Description = description;
		AddressExpression = addressExpression;
		Value = value;
		VariableType = variableType;
		ParentId = parentId;
	}

	/// <summary>Gets the display description to assign.</summary>
	public string Description
	{
		get;
	}

	/// <summary>Gets the CE address expression to assign.</summary>
	public string AddressExpression
	{
		get;
	}

	/// <summary>Gets the CE string value to assign.</summary>
	public string Value
	{
		get;
	}

	/// <summary>Gets the CE value type to assign.</summary>
	public VariableType VariableType
	{
		get;
	}

	/// <summary>Gets the optional parent record to assign after the record fields have been initialized.</summary>
	/// <remarks>A <see langword="null" /> value creates the record at the address-list root.</remarks>
	public MemoryRecordId? ParentId
	{
		get;
	}
}
