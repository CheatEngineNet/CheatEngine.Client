using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Tables;

/// <summary>Copied content fields of a Cheat Engine memory record.</summary>
public readonly record struct MemoryRecordContentSnapshot
{
	/// <summary>Creates copied content fields for a memory-record snapshot.</summary>
	public MemoryRecordContentSnapshot(string description, string addressExpression, string value,
		VariableType variableType)
	{
		ArgumentNullException.ThrowIfNull(description);
		ArgumentNullException.ThrowIfNull(addressExpression);
		ArgumentNullException.ThrowIfNull(value);

		Description = description;
		AddressExpression = addressExpression;
		Value = value;
		VariableType = variableType;
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
}
