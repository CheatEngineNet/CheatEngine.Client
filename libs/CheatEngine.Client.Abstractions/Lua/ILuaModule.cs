namespace CheatEngine.Client.Lua;

/// <summary>Explicitly registers one application-owned set of Lua exports for the current Client activation.</summary>
/// <remarks>
///     <para>
///         <b>Implementable.</b> Applications implement this interface and the Client calls it. Its members are frozen
///         for the 1.x line.
///     </para>
///     <para>
///         The Client invokes both methods synchronously on Cheat Engine's main thread and owns the resulting registration
///         lease. No SDK Lua state, reference, or raw stack access crosses this Client contract.
///     </para>
///     <para>
///         A <see cref="CheatEngineLuaModuleAttribute" /> module calls its SDK-generated <c>RegisterLuaFunctions</c> method
///         and never the legacy <c>UnregisterLuaFunctions</c> helper, which writes <c>nil</c> unconditionally; it releases
///         its globals ownership-aware instead (<see cref="IOwnershipAwareLuaModule" />). A manual implementation may acquire
///         the SDK state and call whatever SDK-generated methods it needs.
///     </para>
/// </remarks>
public interface ILuaModule
{
	/// <summary>Registers this module's exports for the current Cheat Engine activation.</summary>
	/// <remarks>
	///     Throw when the generated SDK registration reports a non-success status so
	///     <see cref="ILuaClient.TryRegisterModule" /> can return the mapped failure.
	///     <para>
	///         A generated module refuses, before any write, to replace a global that is already defined, and refuses to
	///         register again while its previous registration is still current. When a registration fails after the first
	///         write, it rolls back the globals it published (ownership-aware) and throws an exception of the same type as the
	///         original failure whose message starts with the original message; a rollback failure is attached as an
	///         <see cref="AggregateException" /> inner exception whose first element is the original failure. On
	///         CheatEngine.SDK 2.0.0 a combined SDK <c>LuaException</c> cannot carry both a Lua status and an inner exception,
	///         so its <c>Status</c> is <c>Ok</c>; read the original status from that first element. Without a rollback
	///         failure the original exception is thrown itself, status included.
	///     </para>
	///     <para>
	///         A manual module that implements only this interface remains a compatible advanced escape hatch. Because it
	///         does not declare its identity or exports, it cannot participate in the Client's activation-wide global
	///         collision guarantee. Implement <see cref="IDescribedLuaModule" /> for ordinary application modules.
	///     </para>
	/// </remarks>
	public void Register();

	/// <summary>Unregisters this module's exports for the current Cheat Engine activation.</summary>
	/// <remarks>
	///     This is called at most once by the Client, either by disposing the returned lease or during activation
	///     cleanup. Implementations should make their own cleanup safe if an SDK operation reports a failure.
	///     <para>
	///         A generated module is ownership-aware: it writes <c>nil</c> only to globals that still hold the value it
	///         published and never overwrites a global a third party replaced. It attempts every export, publishes the result
	///         through <see cref="IOwnershipAwareLuaModule.LastReleaseOutcome" />, and throws only afterwards when at least
	///         one export failed (one failure is thrown as is, several as an <see cref="AggregateException" />). It consumes
	///         its registration before the first Lua call, so a second call after a completed or failed release is a no-op.
	///         A registration that belongs to an earlier Lua state or attachment is reported as
	///         <see cref="LuaModuleReleaseKind.Stale" /> without any Lua operation.
	///     </para>
	/// </remarks>
	public void Unregister();
}
