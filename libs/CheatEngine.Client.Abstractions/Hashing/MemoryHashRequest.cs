using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Hashing;

/// <summary>Describes a bounded target-memory hash request.</summary>
public readonly record struct MemoryHashRequest
{
	/// <summary>Creates a target-memory hash request.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is not positive or the algorithm is undefined.</exception>
	public MemoryHashRequest(Address address, int length, TargetHashAlgorithm algorithm = TargetHashAlgorithm.Sha256)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
		if (!Enum.IsDefined(algorithm))
		{
			throw new ArgumentOutOfRangeException(nameof(algorithm));
		}

		Address = address;
		Length = length;
		Algorithm = algorithm;
	}

	/// <summary>Gets the first target address included in the hash.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the exact positive number of target bytes included in the hash.</summary>
	public int Length
	{
		get;
	}

	/// <summary>Gets the requested hash algorithm.</summary>
	public TargetHashAlgorithm Algorithm
	{
		get;
	}
}
