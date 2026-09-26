namespace CheatEngine.Client.Runtime;

/// <summary>A stable, Client-owned identifier for one high-level Cheat Engine capability.</summary>
public readonly struct ClientCapabilityId : IEquatable<ClientCapabilityId>
{
	private readonly string? _value;

	/// <summary>Creates a non-empty Client capability identifier.</summary>
	/// <param name="value">The identifier, compared ordinally; the Client's own start with <c>Client.</c>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="value" /> is empty or white space.</exception>
	public ClientCapabilityId(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		_value = value;
	}

	/// <summary>Gets the identifier value, or an empty string for the default value.</summary>
	public string Value => _value ?? string.Empty;

	/// <summary>Gets whether this is the default, unusable identifier.</summary>
	public bool IsEmpty => _value is null;

	/// <summary>Gets the Client capability for process selection and attachment.</summary>
	public static ClientCapabilityId ProcessSelection => new("Client.ProcessSelection");

	/// <summary>Gets the Client capability for typed target-memory operations.</summary>
	public static ClientCapabilityId TypedMemory => new("Client.TypedMemory");

	/// <summary>Gets the Client capability for AOB pattern scanning.</summary>
	public static ClientCapabilityId PatternScanning => new("Client.PatternScanning");

	/// <summary>Gets the Client capability for value scan sessions.</summary>
	public static ClientCapabilityId ValueScanning => new("Client.ValueScanning");

	/// <summary>Gets the Client capability for copied inspection snapshots and symbol operations.</summary>
	public static ClientCapabilityId Inspection => new("Client.Inspection");

	/// <summary>Gets the Client capability for Address List and trusted table operations.</summary>
	public static ClientCapabilityId Tables => new("Client.Tables");

	/// <summary>Gets the Client capability for protected typed Lua operations.</summary>
	public static ClientCapabilityId ProtectedLua => new("Client.ProtectedLua");

	/// <summary>Gets the Client capability for explicitly opted-in arbitrary Lua source execution.</summary>
	public static ClientCapabilityId UnsafeLuaExecution => new("Client.UnsafeLuaExecution");

	/// <summary>Gets the Client capability for owned target-memory allocations.</summary>
	public static ClientCapabilityId Allocations => new("Client.Allocations");

	/// <summary>Gets the Client capability for instruction assembly and disassembly.</summary>
	/// <remarks>
	///     The identifier is stable, but the instruction client it describes (<c>ICheatEngineClient.Assembly</c>) is
	///     experimental (<c>CECLIENT5003</c>).
	/// </remarks>
	public static ClientCapabilityId Assembly => new("Client.Assembly");

	/// <summary>Gets the Client capability for explicitly opted-in Auto Assembler patches.</summary>
	/// <remarks>
	///     The identifier is stable, but the Auto Assembler client it describes is experimental (<c>CECLIENT5004</c>) and
	///     is registered only when the activation calls <c>EnableAutoAssemblerPatches()</c>.
	/// </remarks>
	public static ClientCapabilityId AutoAssemblerPatches => new("Client.AutoAssemblerPatches");

	/// <summary>Tests this identifier and another one for ordinal equality.</summary>
	/// <param name="other">The identifier to compare with.</param>
	/// <returns>
	///     <see langword="true" /> when both identifiers have the same value; two default values are equal.
	/// </returns>
	public bool Equals(ClientCapabilityId other)
	{
		return string.Equals(_value, other._value, StringComparison.Ordinal);
	}

	/// <summary>Tests this identifier and an object for ordinal equality.</summary>
	/// <param name="obj">The object to compare with.</param>
	/// <returns>
	///     <see langword="true" /> when <paramref name="obj" /> is a <see cref="ClientCapabilityId" /> with the same
	///     value.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is ClientCapabilityId other && Equals(other);
	}

	/// <summary>Returns a hash code consistent with the ordinal equality of the value.</summary>
	/// <returns>The ordinal hash code of the value, or zero for the <see langword="default" /> identifier.</returns>
	public override int GetHashCode()
	{
		return _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);
	}

	/// <summary>Returns the identifier value.</summary>
	/// <returns>
	///     <see cref="Value" />: the identifier, or an empty string for the <see langword="default" /> value.
	/// </returns>
	public override string ToString()
	{
		return Value;
	}

	/// <summary>Tests two identifiers for ordinal equality.</summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true" /> when both identifiers have the same value.</returns>
	public static bool operator ==(ClientCapabilityId left, ClientCapabilityId right)
	{
		return left.Equals(right);
	}

	/// <summary>Tests two identifiers for ordinal inequality.</summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true" /> when the identifiers have different values.</returns>
	public static bool operator !=(ClientCapabilityId left, ClientCapabilityId right)
	{
		return !left.Equals(right);
	}
}
