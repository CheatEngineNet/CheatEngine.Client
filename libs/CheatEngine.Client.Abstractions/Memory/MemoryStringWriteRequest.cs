using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A copied string to write to target memory.</summary>
public readonly record struct MemoryStringWriteRequest
{
	/// <summary>Creates a text write.</summary>
	public MemoryStringWriteRequest(Address address, string value, bool wideCharacter = false)
	{
		ArgumentNullException.ThrowIfNull(value);
		Address = address;
		Value = value;
		WideCharacter = wideCharacter;
	}

	/// <summary>Gets the first target address.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the managed text copied by the request.</summary>
	public string Value
	{
		get;
	}

	/// <summary>Gets whether Cheat Engine should write UTF-16 target text.</summary>
	public bool WideCharacter
	{
		get;
	}
}
