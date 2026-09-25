using CheatEngine.Client.Results;

namespace CheatEngine.Client.Lua;

/// <summary>Owns one explicit Lua module registration in a single Cheat Engine client epoch.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <see cref="ICheatEngineLease.Release" /> calls the module's <see cref="ILuaModule.Unregister" /> synchronously on
///         Cheat Engine's main thread and maps its <see cref="LuaModuleReleaseOutcome" /> to the
///         <see cref="LeaseReleaseOutcome" /> of every Client lease: a release that could not begin
///         (<see cref="LeaseReleaseKind.CleanupUnavailable" />) keeps the lease active, a partial release or a registration
///         of an earlier Lua attachment ends it and requires manual recovery, and an exception thrown by the module is an
///         unconfirmed cleanup that is never retried. <see cref="IDisposable.Dispose" /> never throws. Forgotten leases are
///         released in LIFO order while the activation cleanup scope still permits SDK dispatch, and a release that stays
///         incomplete is reported in the aggregated deactivation failure. The lease never exposes a Lua state, Lua
///         reference, or native Lua handle.
///     </para>
/// </remarks>
public interface ILuaModuleLease : ICheatEngineLease
{
	/// <summary>
	///     Gets what the module reported for the last release attempt that reached it, or <see langword="null" /> before
	///     one did.
	/// </summary>
	/// <remarks>
	///     It keeps the counts and failed globals that CheatEngine.SDK observed, which
	///     <see cref="ICheatEngineLease.LastReleaseOutcome" /> summarizes as a kind and a host effect. A release that could
	///     not be dispatched, or whose module threw, leaves it unchanged.
	/// </remarks>
	public LuaModuleReleaseOutcome? ModuleReleaseOutcome
	{
		get;
	}
}
