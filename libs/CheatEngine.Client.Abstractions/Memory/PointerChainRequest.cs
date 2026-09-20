using System.Collections.Immutable;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A finite pointer chain whose offsets are applied after each target-aware pointer dereference.</summary>
public readonly record struct PointerChainRequest
{
	/// <summary>Creates a bounded pointer chain by copying its offsets.</summary>
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
