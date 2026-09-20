using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Inspection;

/// <summary>Describes one explicitly Client-owned symbol registration.</summary>
/// <remarks>
///     Names share Cheat Engine's session-wide symbol table. Callers should use a plugin-specific prefix and retain the
///     resulting lease instead of registering a name already owned by another component.
/// </remarks>
public readonly record struct SymbolRegistration
{
	/// <summary>Creates a custom symbol definition.</summary>
	public SymbolRegistration(string name, Address address, bool doNotSave = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		Name = name;
		Address = address;
		DoNotSave = doNotSave;
	}

	/// <summary>Gets the session-wide symbol name.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets the target address to bind.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets whether Cheat Engine should omit this registration when saving a table.</summary>
	public bool DoNotSave
	{
		get;
	}
}
