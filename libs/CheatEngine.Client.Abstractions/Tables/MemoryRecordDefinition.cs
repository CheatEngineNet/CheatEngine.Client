using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Tables;

/// <summary>The initial managed fields for a newly created Cheat Engine memory record.</summary>
/// <remarks>
///     The <see langword="default" /> value has no field: <see cref="ITableClient.TryCreate" /> throws
///     <see cref="ArgumentException" /> for it before the activation check and before any Cheat Engine call.
/// </remarks>
public readonly record struct MemoryRecordDefinition
{
	/// <summary>Creates a memory-record definition.</summary>
	/// <param name="description">The display description, which can be empty.</param>
	/// <param name="addressExpression">The non-empty Cheat Engine address expression.</param>
	/// <param name="value">The Cheat Engine value text, which can be empty.</param>
	/// <param name="variableType">The defined Cheat Engine value type.</param>
	/// <param name="parentId">The parent record, or <see langword="null" /> for the address-list root.</param>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="description" />, <paramref name="addressExpression" /> or <paramref name="value" /> is
	///     <see langword="null" />.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="addressExpression" /> is empty or white space.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="variableType" /> is not a defined value.
	/// </exception>
	public MemoryRecordDefinition(string description, string addressExpression, string value, VariableType variableType,
		MemoryRecordId? parentId = null)
	{
		ArgumentNullException.ThrowIfNull(description);
		ArgumentException.ThrowIfNullOrWhiteSpace(addressExpression);
		ArgumentNullException.ThrowIfNull(value);
		if (!Enum.IsDefined(variableType))
		{
			throw new ArgumentOutOfRangeException(nameof(variableType), variableType,
				"A memory-record definition assigns a defined value type.");
		}

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
