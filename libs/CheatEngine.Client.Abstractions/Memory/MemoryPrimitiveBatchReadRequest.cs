using System.Collections.Immutable;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A copied, bounded request to read one homogeneous built-in scalar from multiple target addresses.</summary>
/// <typeparam name="T">
///     The built-in scalar or target-aware pointer type to read, one of the types <see cref="IMemoryClient" /> supports.
/// </typeparam>
public readonly struct MemoryPrimitiveBatchReadRequest<T>
	where T : unmanaged
{
	/// <summary>Creates a bounded batch by copying every target address.</summary>
	/// <param name="addresses">The non-empty target addresses to read in order.</param>
	/// <exception cref="ArgumentException"><paramref name="addresses" /> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="addresses" /> exceeds the supported batch size.</exception>
	public MemoryPrimitiveBatchReadRequest(ReadOnlySpan<Address> addresses)
	{
		if (addresses.IsEmpty)
		{
			throw new ArgumentException("A memory batch requires at least one address.", nameof(addresses));
		}

		if (addresses.Length > MemoryBatchLimits.MaximumOperationCount)
		{
			throw new ArgumentOutOfRangeException(nameof(addresses),
				$"A memory batch is limited to {MemoryBatchLimits.MaximumOperationCount} operations.");
		}

		Addresses = ImmutableArray.Create(addresses.ToArray());
	}

	/// <summary>Gets the copied target addresses in result order.</summary>
	public ImmutableArray<Address> Addresses
	{
		get;
	}
}
