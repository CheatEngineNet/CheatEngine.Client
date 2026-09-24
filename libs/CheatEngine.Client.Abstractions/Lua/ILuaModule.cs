namespace CheatEngine.Client.Lua;

/// <summary>Registers and releases one application-owned set of Lua globals for the current Client activation.</summary>
/// <remarks>
///     <para>
///         <b>Implementable.</b> Applications implement this interface and the Client calls it. Its members are frozen
///         for the 1.x line.
///     </para>
///     <para>
///         The Client calls <see cref="Register" /> and <see cref="Unregister" /> synchronously on Cheat Engine's main
///         thread, through <see cref="ILuaClient.RegisterModule" /> and the returned <see cref="ILuaModuleLease" />, and
///         owns the resulting lease. Before <see cref="Register" /> runs, the Client reserves <see cref="Descriptor" />'s
///         module name and every export name for the activation, so two modules of one activation never claim the same
///         Lua global. No SDK Lua state, reference, or raw stack access crosses this Client contract.
///     </para>
///     <para>
///         A <see cref="CheatEngineLuaModuleAttribute" /> module generates all three members. It registers through the
///         SDK-generated <c>TryRegisterLuaFunctions</c> of its bindings type with the <c>RejectExisting</c> collision
///         policy, holds the CheatEngine.SDK registration lease, and releases it ownership-aware: a global is written only
///         while it still holds the value the module installed. It never calls the legacy SDK
///         <c>RegisterLuaFunctions</c>/<c>UnregisterLuaFunctions</c> pair, which writes unconditionally. A manual
///         implementation declares its exports truthfully in <see cref="Descriptor" /> and reports its release with the
///         <see cref="LuaModuleReleaseOutcome" /> factories.
///     </para>
/// </remarks>
public interface ILuaModule
{
	/// <summary>Gets this module's stable identity and the Lua global names it exports.</summary>
	/// <remarks>
	///     The descriptor is copied metadata only: no Lua state, SDK ownership, reference, callback, or native handle.
	/// </remarks>
	public LuaModuleDescriptor Descriptor
	{
		get;
	}

	/// <summary>Registers this module's exports for the current Cheat Engine activation.</summary>
	/// <remarks>
	///     <para>
	///         Throw to refuse the registration; <see cref="ILuaClient.TryRegisterModule" /> returns the failure. A
	///         <see cref="Results.CheatEngineOperationException" /> is reported with the failure it carries (one that
	///         carries the <see langword="default" /> failure is reported as <c>Unknown</c>); any other exception is
	///         classified by the Client like a CheatEngine.SDK fault, because a generated module surfaces the SDK faults
	///         of its registration from this method.
	///     </para>
	///     <para>
	///         A generated module throws a <see cref="Results.CheatEngineOperationException" /> when CheatEngine.SDK
	///         refuses the Lua admission (<c>ActivationExpired</c>, <c>RuntimeChanged</c> or <c>InvalidState</c>, with the
	///         host effect <c>NotStarted</c>), when a global is already defined (<c>OperationRejected</c>,
	///         <c>NotApplied</c>: nothing was published), or when a protected lookup or publication failed
	///         (<c>LuaError</c>; <c>NotApplied</c> when the SDK's rollback removed everything it published,
	///         <c>CleanupUnconfirmed</c> otherwise). Registering a module that still owns a registration releases it
	///         first; when that release may leave one of its globals, nothing is published and the failure is
	///         <c>CleanupUnconfirmed</c>.
	///     </para>
	/// </remarks>
	public void Register();

	/// <summary>Releases this module's exports for the current Cheat Engine activation and reports what happened.</summary>
	/// <returns>The copied release outcome; never <see langword="null" />.</returns>
	/// <remarks>
	///     <para>
	///         The Client calls it once for each successful <see cref="Register" />, and again only after an outcome that
	///         left the registration in place (<see cref="Results.LeaseReleaseKind.CleanupUnavailable" /> or
	///         <see cref="Results.LeaseReleaseKind.Unknown" />). Report a release that failed in the outcome rather than by
	///         throwing: an exception is recorded as an unconfirmed cleanup and never retried.
	///     </para>
	///     <para>
	///         A generated module writes only globals that still hold the value it installed and never overwrites one a
	///         third party replaced. It returns <see cref="Results.LeaseReleaseKind.AlreadyReleased" /> without any Lua call
	///         when it owns no registration. When CheatEngine.SDK refuses the Lua admission because the Lua universe that
	///         holds the registration is gone (<c>Detached</c> or <c>ExternalStateReset</c>), it consumes the registration
	///         without any Lua call and returns <see cref="Results.LeaseReleaseKind.RefusedRuntimeChanged" />; any other
	///         refused admission keeps the registration and returns
	///         <see cref="Results.LeaseReleaseKind.CleanupUnavailable" />. Once it has consumed the registration it never
	///         returns a retryable kind: a release that CheatEngine.SDK reports outside its documented shape is
	///         <see cref="Results.LeaseReleaseKind.CleanupUnconfirmed" />.
	///     </para>
	/// </remarks>
	public LuaModuleReleaseOutcome Unregister();
}
