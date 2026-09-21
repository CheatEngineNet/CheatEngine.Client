namespace CheatEngine.Client.Lua;

/// <summary>Copied metadata that identifies one Lua global exported by a Client module.</summary>
public readonly record struct LuaExportDescriptor
{
	/// <summary>Initializes one validated Lua global descriptor.</summary>
	/// <param name="name">The case-sensitive Lua global name.</param>
	public LuaExportDescriptor(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
	}

	/// <summary>Gets the case-sensitive Lua global name.</summary>
	public string Name
	{
		get;
	}
}
