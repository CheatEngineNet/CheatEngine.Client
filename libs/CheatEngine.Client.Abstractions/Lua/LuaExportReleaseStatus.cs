namespace CheatEngine.Client.Lua;

/// <summary>Classifies what the release of one Lua module export observed and did.</summary>
/// <remarks>
///     Generated <see cref="CheatEngineLuaModuleAttribute" /> modules compare the current global with the value they
///     published by primitive identity (no <c>__eq</c> metamethod) and clear it only while it is still theirs. The values
///     mirror the per-entry vocabulary of the CheatEngine.SDK 2.0 registration leases so the SDK 2.0 migration is a
///     mapping, not a redesign.
/// </remarks>
public enum LuaExportReleaseStatus
{
	/// <summary>Contract hygiene: never produced by a release and rejected by <see cref="LuaExportReleaseOutcome" />.</summary>
	Unknown = 0,

	/// <summary>The global still held the module's own value and was set to <c>nil</c>.</summary>
	Removed = 1,

	/// <summary>
	///     The global held a different, non-<c>nil</c> value (a third party replaced it, even with a wrapper of the module's
	///     function); nothing was written.
	/// </summary>
	Replaced = 2,

	/// <summary>The global was already <c>nil</c>; nothing was written.</summary>
	Absent = 3,

	/// <summary>
	///     Reading, comparing, or clearing the global failed; the global may still hold the module's value. The failure is
	///     thrown by <see cref="ILuaModule.Unregister" /> after every export was attempted.
	/// </summary>
	Failed = 4,

	/// <summary>
	///     No Lua operation was attempted because the registration belongs to an earlier Lua state or attachment; the
	///     current state's globals are not the module's.
	/// </summary>
	NotAttempted = 5
}
