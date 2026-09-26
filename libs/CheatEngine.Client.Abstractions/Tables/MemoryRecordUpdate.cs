using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Tables;

/// <summary>A partial change set for an existing Cheat Engine memory record.</summary>
/// <remarks>
///     The record it changes is the <see cref="MemoryRecordId" /> passed to <see cref="ITableClient.TryUpdate" />,
///     first like every record-targeting operation. The <see langword="default" /> value changes nothing, and
///     <see cref="ITableClient.TryUpdate" /> throws <see cref="ArgumentException" /> for it before the activation check
///     and before any Cheat Engine call.
/// </remarks>
public readonly record struct MemoryRecordUpdate
{
	/// <summary>Creates a memory-record update containing at least one changed field.</summary>
	/// <param name="description">The replacement description, or <see langword="null" /> to keep it.</param>
	/// <param name="addressExpression">A non-empty replacement address expression, or <see langword="null" />.</param>
	/// <param name="value">The replacement value text, or <see langword="null" /> to keep it.</param>
	/// <param name="variableType">The replacement value type, or <see langword="null" /> to keep it.</param>
	/// <exception cref="ArgumentException">
	///     Every field is <see langword="null" />, or <paramref name="addressExpression" /> is empty.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="variableType" /> is not a defined value.
	/// </exception>
	public MemoryRecordUpdate(
		string? description = null,
		string? addressExpression = null,
		string? value = null,
		VariableType? variableType = null)
	{
		if (variableType is { } type && !Enum.IsDefined(type))
		{
			throw new ArgumentOutOfRangeException(nameof(variableType), type,
				"A memory-record update assigns a defined value type.");
		}

		if (description is null && addressExpression is null && value is null && variableType is null)
		{
			throw new ArgumentException("A memory-record update must change at least one field.", nameof(description));
		}

		if (addressExpression is { Length: 0 })
		{
			throw new ArgumentException("An address expression must be null or non-empty.", nameof(addressExpression));
		}

		Description = description;
		AddressExpression = addressExpression;
		Value = value;
		VariableType = variableType;
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
