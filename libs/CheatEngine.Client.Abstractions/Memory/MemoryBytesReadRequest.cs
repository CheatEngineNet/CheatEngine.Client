using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A bounded request to copy a fixed number of target bytes.</summary>
public readonly record struct MemoryBytesReadRequest
{
	/// <summary>Creates a bounded byte read.</summary>
	/// <param name="address">The first target address.</param>
	/// <param name="length">The positive number of bytes to copy.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is zero or negative.</exception>
	public MemoryBytesReadRequest(Address address, int length)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
		Address = address;
		Length = length;
	}

	/// <summary>Gets the first target address.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the exact number of bytes to copy.</summary>
	public int Length
	{
		get;
	}
}
