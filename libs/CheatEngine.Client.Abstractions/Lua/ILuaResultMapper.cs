namespace CheatEngine.Client.Lua;

/// <summary>
///     Projects one SDK-returned value into a copied Client result while a typed Lua operation is still active.
/// </summary>
/// <typeparam name="TSource">The SDK binding result type visible only inside generated operation code.</typeparam>
/// <typeparam name="TResult">The copied Client result type exposed by the operation.</typeparam>
/// <remarks>
///     <para>
///         <b>Implementable.</b> Applications implement this interface and the Client calls it. Its members are frozen
///         for the 1.x line.
///     </para>
///     <para>
///         Implementations must not retain their source value when it is backed by a Lua reference, CE object, owned
///         wrapper, native pointer, span, or other activation-bound SDK resource. The generated operation invokes this
///         member directly through static abstract interface dispatch; it never uses reflection.
///     </para>
/// </remarks>
public interface ILuaResultMapper<TSource, TResult>
{
	/// <summary>Creates the Client-owned result before the Lua operation scope ends.</summary>
	/// <param name="source">The value returned by the SDK binding.</param>
	/// <returns>A copied Client result.</returns>
	public static abstract TResult Map(TSource source);
}
