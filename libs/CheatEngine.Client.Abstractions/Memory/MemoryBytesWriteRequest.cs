using System.Collections.Immutable;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A copied byte sequence to write to target memory.</summary>
public readonly struct MemoryBytesWriteRequest
{
	/// <summary>Creates a byte write and copies the caller's data immediately.</summary>
	public MemoryBytesWriteRequest(Address address, ReadOnlySpan<byte> bytes)
	{
		if (bytes.IsEmpty)
		{
			throw new ArgumentException("At least one byte is required.", nameof(bytes));
		}

		Address = address;
		Bytes = ImmutableArray.Create(bytes.ToArray());
	}

	/// <summary>Gets the first target address.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the independent immutable bytes.</summary>
	public ImmutableArray<byte> Bytes
	{
		get;
	}
}
