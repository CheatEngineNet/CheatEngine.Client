namespace CheatEngine.Client.Lua;

/// <summary>An immutable Lua chunk to execute under the SDK's protected Lua boundary.</summary>
public readonly record struct LuaScript
{
	/// <summary>Creates a Lua script request.</summary>
	/// <param name="source">The Lua source text.</param>
	/// <param name="chunkName">The chunk name Lua diagnostics use, or <see langword="null" /> for none.</param>
	/// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="chunkName" /> is empty.</exception>
	public LuaScript(string source, string? chunkName = null)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (chunkName is { Length: 0 })
		{
			throw new ArgumentException("A Lua chunk name must be null or non-empty.", nameof(chunkName));
		}

		Source = source;
		ChunkName = chunkName;
	}

	/// <summary>Gets the UTF-16 source text to encode as UTF-8 for Lua.</summary>
	public string Source
	{
		get;
	}

	/// <summary>Gets the optional chunk name used in Lua diagnostics.</summary>
	public string? ChunkName
	{
		get;
	}
}
