namespace CheatEngine.Client.Runtime;

/// <summary>A stable, Client-owned identifier for one high-level Cheat Engine capability.</summary>
public readonly struct ClientCapabilityId : IEquatable<ClientCapabilityId>
{
	private readonly string? _value;

	/// <summary>Creates a non-empty Client capability identifier.</summary>
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

	/// <summary>Gets the Client capability for assembly, disassembly, comments, and Auto Assembler patches.</summary>
	public static ClientCapabilityId Assembly => new("Client.Assembly");

	/// <summary>Gets the Client capability for bounded remote execution and DLL injection.</summary>
	public static ClientCapabilityId RemoteExecution => new("Client.RemoteExecution");

	/// <summary>Gets the Client capability for debugger breakpoints and copied debugger events.</summary>
	public static ClientCapabilityId Debugger => new("Client.Debugger");

	/// <summary>Gets the Client capability for activation-scoped hotkeys.</summary>
	public static ClientCapabilityId Hotkeys => new("Client.Hotkeys");

	/// <summary>Gets the Client capability for activation-scoped timers.</summary>
	public static ClientCapabilityId Timers => new("Client.Timers");

	/// <summary>Gets the Client capability for observing and changing target speed.</summary>
	public static ClientCapabilityId Speed => new("Client.Speed");

	/// <summary>Gets the Client capability for target-memory and file hashing.</summary>
	public static ClientCapabilityId Hashing => new("Client.Hashing");

	/// <summary>Gets the Client capability for explicitly initialized DBVM operations and watches.</summary>
	public static ClientCapabilityId Dbvm => new("Client.Dbvm");

	/// <inheritdoc />
	public bool Equals(ClientCapabilityId other)
	{
		return string.Equals(_value, other._value, StringComparison.Ordinal);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is ClientCapabilityId other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return Value;
	}

	/// <summary>Tests two identifiers for ordinal equality.</summary>
	public static bool operator ==(ClientCapabilityId left, ClientCapabilityId right)
	{
		return left.Equals(right);
	}

	/// <summary>Tests two identifiers for ordinal inequality.</summary>
	public static bool operator !=(ClientCapabilityId left, ClientCapabilityId right)
	{
		return !left.Equals(right);
	}
}
