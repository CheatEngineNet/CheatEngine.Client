namespace CheatEngine.Client.Lua;

/// <summary>
///     A Lua module whose <see cref="ILuaModule.Unregister" /> clears a global only while it still holds the value the module
///     published, and reports what each export release observed.
/// </summary>
/// <remarks>
///     <para>
///         <b>Implementable.</b> Applications implement this interface and the Client calls it. Its members are frozen
///         for the 1.x line.
///     </para>
///     <para>
///         Every <see cref="CheatEngineLuaModuleAttribute" /> module implements this contract. After registration the
///         generated module pins the value it published under each export; at release it compares the current global with
///         that value by primitive identity and writes <c>nil</c> only when the global is still the module's. A third-party
///         replacement, including a wrapper of the module's function, is left untouched and reported as
///         <see cref="LuaExportReleaseStatus.Replaced" />.
///     </para>
///     <para>
///         The outcome contains copied names and statuses only; it never exposes a Lua state, reference, or native handle.
///     </para>
/// </remarks>
public interface IOwnershipAwareLuaModule : ILuaModule
{
	/// <summary>
	///     Gets the outcome of the most recent <see cref="ILuaModule.Unregister" /> that released an owned registration
	///     through Lua or detected a stale registration, or <see langword="null" /> before any.
	/// </summary>
	/// <remarks>
	///     The outcome is published before <see cref="ILuaModule.Unregister" /> throws a Lua failure, so a caller that catches
	///     the release failure can still read which exports were removed, replaced, absent, or failed. A call to
	///     <see cref="ILuaModule.Unregister" /> that finds no owned registration leaves this value unchanged, and so does a
	///     programming or lifecycle exception that escapes the release before its end. The rollback of a failed
	///     <see cref="ILuaModule.Register" /> also releases through Lua but publishes no outcome: its result is the exception
	///     <see cref="ILuaModule.Register" /> throws. Safe to read from any thread.
	/// </remarks>
	public LuaModuleReleaseOutcome? LastReleaseOutcome
	{
		get;
	}
}
