namespace CheatEngine.Client.Lua;

/// <summary>
///     Declares a Client-owned Lua module whose registration adapter is emitted at compile time.
/// </summary>
/// <remarks>
///     <para>
///         The annotated type is a non-static partial class. <paramref name="bindingsType" /> names the static partial
///         SDK binding type that owns the SDK-generated <c>RegisterLuaFunctions</c> method. The Client generator reads this
///         declaration at compile time; it never discovers modules through reflection at run time.
///     </para>
///     <para>
///         The generated module implements <see cref="IDescribedLuaModule" /> and <see cref="IOwnershipAwareLuaModule" />.
///         It never calls the legacy <c>UnregisterLuaFunctions</c> helper: at release it clears each exported global only
///         while the global still holds the value the module published, leaves a third-party replacement untouched, and
///         reports the result through <see cref="IOwnershipAwareLuaModule.LastReleaseOutcome" />.
///     </para>
///     <para>
///         The optional name is the stable identity reserved by <see cref="ILuaClient" /> for one activation. When it
///         is omitted, the generator uses the annotated module type's simple name. Lua export identities are copied
///         from the <c>[LuaFunction]</c> declarations on <paramref name="bindingsType" />.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CheatEngineLuaModuleAttribute(Type bindingsType, string? name = null) : Attribute
{
	/// <summary>Gets the static SDK binding type that owns the SDK-generated Lua registration method.</summary>
	public Type BindingsType
	{
		get;
	} = bindingsType ?? throw new ArgumentNullException(nameof(bindingsType));

	/// <summary>Gets the optional stable module identity, or <see langword="null" /> to use the module type name.</summary>
	public string? Name
	{
		get;
	} = name;
}
