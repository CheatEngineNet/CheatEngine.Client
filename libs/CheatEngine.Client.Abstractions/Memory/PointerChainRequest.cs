using System.Collections.Immutable;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A finite pointer chain whose offsets are applied after each target-aware pointer dereference.</summary>
public readonly struct PointerChainRequest
{
	/// <summary>Creates a bounded pointer chain by copying its offsets.</summary>
	/// <param name="baseAddress">The address that holds the first target pointer.</param>
	/// <param name="offsets">The offsets, one per pointer read: between 1 and 64, copied by the constructor.</param>
	/// <exception cref="ArgumentException"><paramref name="offsets" /> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offsets" /> has more than 64 offsets.</exception>
	public PointerChainRequest(Address baseAddress, ReadOnlySpan<long> offsets)
	{
		if (offsets.IsEmpty)
		{
			throw new ArgumentException("A pointer chain requires at least one offset.", nameof(offsets));
		}

		if (offsets.Length > 64)
		{
			throw new ArgumentOutOfRangeException(nameof(offsets), "A pointer chain is limited to 64 hops.");
		}

		BaseAddress = baseAddress;
		Offsets = ImmutableArray.Create(offsets.ToArray());
	}

	/// <summary>Gets the address containing the first target pointer.</summary>
	public Address BaseAddress
	{
		get;
	}

	/// <summary>Gets the finite offsets, one per dereference.</summary>
	public ImmutableArray<long> Offsets
	{
		get;
	}
}
