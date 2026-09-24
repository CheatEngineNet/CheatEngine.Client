using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>Maps one managed value type to bounded target-memory operations.</summary>
/// <typeparam name="T">The managed value type.</typeparam>
/// <remarks>
///     <para>
///         <b>Implementable.</b> Applications implement this interface and the Client calls it. Its members are frozen
///         for the 1.x line.
///     </para>
///     <para>
///         Implementations must be deterministic, allocation-conscious, AOT-safe, and independent of Lua or CE object
///         handles. A codec is invoked synchronously on Cheat Engine's dispatch boundary and must not retain its
///         context.
///     </para>
/// </remarks>
public interface IMemoryCodec<T>
{
	/// <summary>Tries to read one value from the requested target address.</summary>
	public bool TryRead(IMemoryReadContext context, Address address, [MaybeNullWhen(false)] out T value);

	/// <summary>Tries to write one value to the requested target address.</summary>
	public bool TryWrite(IMemoryWriteContext context, Address address, in T value);
}
