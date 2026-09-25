using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>The fluent entry points of the target-memory domain, bound to a scoped <see cref="IMemoryClient" />.</summary>
/// <remarks>
///     A memory builder always starts from the service that runs its terminal operations, usually <c>client.Memory</c>
///     of an activation-scoped <see cref="ICheatEngineClient" />: it is never created unbound and never rebound.
/// </remarks>
public static class CheatEngineMemoryFluentExtensions
{
	/// <summary>Starts a fluent, immutable operation against <paramref name="address" />.</summary>
	/// <param name="memory">The scoped target-memory service used by terminal operations.</param>
	/// <param name="address">The target address to read or write.</param>
	/// <returns>An immutable address builder bound to <paramref name="memory" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="memory" /> is <see langword="null" />.</exception>
	public static MemoryAddressBuilder At(this IMemoryClient memory, Address address)
	{
		ArgumentNullException.ThrowIfNull(memory);
		return new MemoryAddressBuilder(address, memory);
	}

	/// <summary>Starts a bounded homogeneous primitive batch through the scoped target-memory service.</summary>
	/// <typeparam name="T">
	///     The built-in scalar or target-aware pointer type, one of the primitives <see cref="IMemoryClient" /> supports.
	/// </typeparam>
	/// <param name="memory">The scoped target-memory service used by terminal operations.</param>
	/// <returns>An immutable batch builder bound to <paramref name="memory" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="memory" /> is <see langword="null" />.</exception>
	public static MemoryPrimitiveBatchBuilder<T> Batch<T>(this IMemoryClient memory)
		where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(memory);
		return new MemoryPrimitiveBatchBuilder<T>(memory);
	}
}
