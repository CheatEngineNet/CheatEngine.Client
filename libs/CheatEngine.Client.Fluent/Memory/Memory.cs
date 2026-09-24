using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>Starts a fluent, handle-free operation against one target-memory address.</summary>
public static class Memory
{
	/// <summary>Creates an unbound address builder.</summary>
	/// <param name="address">The target address to read or write.</param>
	/// <returns>
	///     An immutable address builder that must be bound with <c>Using(memory)</c>
	///     before a built-in terminal operation.
	/// </returns>
	public static MemoryAddressBuilder At(Address address)
	{
		return new MemoryAddressBuilder(address, null);
	}

	/// <summary>Creates an address builder bound to the supplied memory service.</summary>
	/// <param name="memory">The scoped target-memory service used by terminal operations.</param>
	/// <param name="address">The target address to read or write.</param>
	/// <returns>An immutable address builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="memory" /> is <see langword="null" />.</exception>
	public static MemoryAddressBuilder At(IMemoryClient memory, Address address)
	{
		ArgumentNullException.ThrowIfNull(memory);
		return new MemoryAddressBuilder(address, memory);
	}

	/// <summary>Creates a fluent terminal for one bounded homogeneous primitive batch.</summary>
	/// <typeparam name="T">
	///     The built-in scalar or target-aware pointer type, one of the primitives <see cref="IMemoryClient" /> supports.
	/// </typeparam>
	/// <param name="memory">The scoped target-memory service used by terminal operations.</param>
	/// <returns>An immutable batch builder bound to <paramref name="memory" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="memory" /> is <see langword="null" />.</exception>
	public static MemoryPrimitiveBatchBuilder<T> Batch<T>(IMemoryClient memory)
		where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(memory);
		return new MemoryPrimitiveBatchBuilder<T>(memory);
	}
}
