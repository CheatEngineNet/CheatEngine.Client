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
}
