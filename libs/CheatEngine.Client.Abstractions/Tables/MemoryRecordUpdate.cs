using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Tables;

/// <summary>A partial change set for an existing Cheat Engine memory record.</summary>
public readonly record struct MemoryRecordUpdate
{
	/// <summary>Creates a memory-record update containing at least one changed field.</summary>
	public MemoryRecordUpdate(
		MemoryRecordId id,
		string? description = null,
		string? addressExpression = null,
		string? value = null,
		VariableType? variableType = null)
	{
		if (description is null && addressExpression is null && value is null && variableType is null)
		{
			throw new ArgumentException("A memory-record update must change at least one field.", nameof(description));
		}

		if (addressExpression is { Length: 0 })
		{
			throw new ArgumentException("An address expression must be null or non-empty.", nameof(addressExpression));
		}

		Id = id;
		Description = description;
		AddressExpression = addressExpression;
		Value = value;
		VariableType = variableType;
	}

	/// <summary>Gets the identifier of the record to change.</summary>
	public MemoryRecordId Id
	{
		get;
	}

	/// <summary>Gets the optional replacement description.</summary>
	public string? Description
	{
		get;
	}

	/// <summary>Gets the optional replacement address expression.</summary>
	public string? AddressExpression
	{
		get;
	}

	/// <summary>Gets the optional replacement value text.</summary>
	public string? Value
	{
		get;
	}

	/// <summary>Gets the optional replacement value type.</summary>
	public VariableType? VariableType
	{
		get;
	}
}
