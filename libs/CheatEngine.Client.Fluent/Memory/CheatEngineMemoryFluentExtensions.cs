using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>Fluent entry points for the scoped target-memory contract.</summary>
public static class CheatEngineMemoryFluentExtensions
{
	/// <summary>Starts a fluent, immutable operation against <paramref name="address" />.</summary>
	/// <param name="memory">The scoped target-memory service used by terminal operations.</param>
	/// <param name="address">The target address to read or write.</param>
	/// <returns>An immutable address builder bound to <paramref name="memory" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="memory" /> is <see langword="null" />.</exception>
	public static MemoryAddressBuilder At(this IMemoryClient memory, Address address)
	{
		return Memory.At(memory, address);
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
		return Memory.Batch<T>(memory);
	}
}
