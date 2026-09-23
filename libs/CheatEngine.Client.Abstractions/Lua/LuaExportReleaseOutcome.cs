namespace CheatEngine.Client.Lua;

/// <summary>Copied, handle-free result of releasing one Lua global exported by a module.</summary>
public readonly record struct LuaExportReleaseOutcome
{
	/// <summary>Initializes one validated export release result.</summary>
	/// <param name="name">The case-sensitive Lua global name.</param>
	/// <param name="status">What the release observed and did for this global.</param>
	/// <exception cref="ArgumentException"><paramref name="name" /> is <see langword="null" />, empty, or whitespace.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="status" /> is <see cref="LuaExportReleaseStatus.Unknown" /> or not a defined value.
	/// </exception>
	public LuaExportReleaseOutcome(string name, LuaExportReleaseStatus status)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (status is <= LuaExportReleaseStatus.Unknown or > LuaExportReleaseStatus.NotAttempted)
		{
			throw new ArgumentOutOfRangeException(nameof(status), status,
				"A Lua export release status must be a defined value other than Unknown.");
		}

		Name = name;
		Status = status;
	}

	/// <summary>Gets the case-sensitive Lua global name.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets what the release observed and did for this global.</summary>
	public LuaExportReleaseStatus Status
	{
		get;
	}
}
