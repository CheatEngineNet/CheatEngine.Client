using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;
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
	/// <param name="context">The bounded read context of this invocation; never retain it.</param>
	/// <param name="address">The target address.</param>
	/// <param name="value">The value read when the method returns <see langword="true" />.</param>
	/// <param name="failure">
	///     When the method returns <see langword="false" />: the classified failure the Client publishes unchanged,
	///     including its host effect (a failure a context method returned can be passed on as is), or the
	///     <see langword="default" /> value to let the Client classify the failure from what the context observed.
	/// </param>
	/// <returns><see langword="true" /> when the value was read.</returns>
	public bool TryRead(IMemoryReadContext context, Address address, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure);

	/// <summary>Tries to write one value to the requested target address.</summary>
	/// <param name="context">The bounded write context of this invocation; never retain it.</param>
	/// <param name="address">The target address.</param>
	/// <param name="value">The value to write.</param>
	/// <param name="failure">
	///     When the method returns <see langword="false" />: the classified failure the Client publishes unchanged,
	///     including its host effect, or the <see langword="default" /> value to let the Client classify the failure from
	///     what the context observed.
	/// </param>
	/// <returns><see langword="true" /> when the value was written.</returns>
	public bool TryWrite(IMemoryWriteContext context, Address address, in T value, out CheatEngineFailure failure);
}
