using System.Collections.Immutable;

namespace CheatEngine.Client.Memory;

/// <summary>A copied, bounded request to write one homogeneous built-in scalar to multiple target addresses.</summary>
/// <typeparam name="T">
///     The built-in scalar or target-aware pointer type to write, one of the types <see cref="IMemoryClient" /> supports.
/// </typeparam>
public readonly struct MemoryPrimitiveBatchWriteRequest<T>
	where T : unmanaged
{
	/// <summary>Creates a bounded batch by copying every address/value pair.</summary>
	/// <param name="values">The non-empty target writes to perform in order.</param>
	/// <exception cref="ArgumentException"><paramref name="values" /> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="values" /> exceeds the supported batch size.</exception>
	public MemoryPrimitiveBatchWriteRequest(ReadOnlySpan<MemoryAddressValue<T>> values)
	{
		if (values.IsEmpty)
		{
			throw new ArgumentException("A memory batch requires at least one value.", nameof(values));
		}

		if (values.Length > MemoryBatchLimits.MaximumOperationCount)
		{
			throw new ArgumentOutOfRangeException(nameof(values),
				$"A memory batch is limited to {MemoryBatchLimits.MaximumOperationCount} operations.");
		}

		Values = ImmutableArray.Create(values.ToArray());
	}

	/// <summary>Gets the copied address/value pairs in execution order.</summary>
	public ImmutableArray<MemoryAddressValue<T>> Values
	{
		get;
	}
}
