namespace CheatEngine.Client.Lua;

/// <summary>
///     Describes an explicit Lua module before the Client mutates Cheat Engine's global Lua environment.
/// </summary>
/// <remarks>
///     <para>
///         <b>Implementable.</b> Applications implement this interface and the Client calls it. Its members are frozen
///         for the 1.x line.
///     </para>
///     <para>
///         The descriptor is copied metadata only. It intentionally contains neither a Lua state nor any SDK ownership,
///         reference, callback, or native handle. <see cref="ILuaClient" /> uses it to reserve the module and all
///         export names atomically for an activation before invoking <see cref="ILuaModule.Register" />.
///     </para>
/// </remarks>
public interface IDescribedLuaModule : ILuaModule
{
	/// <summary>Gets this module's stable identity and exported Lua global names.</summary>
	public LuaModuleDescriptor Descriptor
	{
		get;
	}
}
