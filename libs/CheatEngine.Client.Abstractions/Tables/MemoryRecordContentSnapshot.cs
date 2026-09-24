using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Tables;

/// <summary>Copied content fields of a Cheat Engine memory record.</summary>
public readonly record struct MemoryRecordContentSnapshot
{
	/// <summary>Creates copied content fields for a memory-record snapshot.</summary>
	/// <param name="description">The record display description.</param>
	/// <param name="addressExpression">The record's unresolved Cheat Engine address expression.</param>
	/// <param name="value">The record's verbatim value text.</param>
	/// <param name="variableType">The record's Cheat Engine value type.</param>
	/// <param name="script">The record's Auto Assembler script, or <see langword="null" /> when it has none.</param>
	/// <param name="offsetCount">The number of pointer offsets of the address; 0 for a plain address.</param>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="description" />, <paramref name="addressExpression" /> or <paramref name="value" /> is
	///     <see langword="null" />.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offsetCount" /> is negative.</exception>
	public MemoryRecordContentSnapshot(string description, string addressExpression, string value,
		VariableType variableType, string? script = null, int offsetCount = 0)
	{
		ArgumentNullException.ThrowIfNull(description);
		ArgumentNullException.ThrowIfNull(addressExpression);
		ArgumentNullException.ThrowIfNull(value);
		ArgumentOutOfRangeException.ThrowIfNegative(offsetCount);

		Description = description;
		AddressExpression = addressExpression;
		Value = value;
		VariableType = variableType;
		Script = script;
		OffsetCount = offsetCount;
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

	/// <summary>Gets the Auto Assembler script of the record (Cheat Engine's <c>Script</c> property).</summary>
	/// <remarks>
	///     <see langword="null" /> when Cheat Engine returned no script text, which is the case of a record that is not an
	///     Auto Assembler script, or when the script could not be read: CheatEngine.SDK reports a failed read of
	///     <c>Script</c> the same way as a record without one. <see langword="null" /> therefore does not prove that an
	///     Auto Assembler record has no script, and unlike the other fields a failed <c>Script</c> read does not fail the
	///     snapshot.
	/// </remarks>
	public string? Script
	{
		get;
	}

	/// <summary>Gets the number of pointer offsets of the record's address; 0 for a plain address.</summary>
	public int OffsetCount
	{
		get;
	}
}
