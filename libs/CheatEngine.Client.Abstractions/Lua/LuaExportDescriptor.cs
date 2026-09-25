namespace CheatEngine.Client.Lua;

/// <summary>Copied metadata that identifies one Lua global exported by a Client module.</summary>
public readonly record struct LuaExportDescriptor
{
	private readonly string? _name;

	/// <summary>Initializes one validated Lua global descriptor.</summary>
	/// <param name="name">The case-sensitive Lua global name.</param>
	/// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="name" /> is empty or white space.</exception>
	public LuaExportDescriptor(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		_name = name;
	}

	/// <summary>Gets the case-sensitive Lua global name.</summary>
	/// <remarks><see cref="string.Empty" /> for the <see langword="default" /> value.</remarks>
	public string Name => _name ?? string.Empty;
}
