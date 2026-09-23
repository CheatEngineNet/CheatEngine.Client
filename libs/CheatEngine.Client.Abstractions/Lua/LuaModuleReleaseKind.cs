namespace CheatEngine.Client.Lua;

/// <summary>Classifies a completed release of a Lua module registration.</summary>
/// <remarks>
///     Values 1 to 3 have the same meaning and number as the CheatEngine.SDK 2.0 <c>LuaRegistrationReleaseKind</c>, so the
///     SDK 2.0 migration maps them one to one.
/// </remarks>
public enum LuaModuleReleaseKind
{
	/// <summary>Contract hygiene: never produced by <see cref="LuaModuleReleaseOutcome" />.</summary>
	Unknown = 0,

	/// <summary>
	///     Every export was examined: still-owned exports were removed, replaced or already absent exports were left
	///     alone.
	/// </summary>
	Released = 1,

	/// <summary>
	///     At least one export failed; the other exports were still attempted and the failures were thrown after the
	///     outcome was published.
	/// </summary>
	PartiallyReleased = 2,

	/// <summary>The registration belongs to an earlier Lua state or attachment, so no Lua operation was attempted.</summary>
	Stale = 3
}
