using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable typed target-memory read request.</summary>
public readonly record struct MemoryReadRequest<T>
{
	/// <summary>Creates a memory read request.</summary>
	public MemoryReadRequest(Address address, IMemoryCodec<T> codec)
	{
		ArgumentNullException.ThrowIfNull(codec);
		Address = address;
		Codec = codec;
	}

	/// <summary>Gets the target address to read.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the codec used to translate the value.</summary>
	public IMemoryCodec<T> Codec
	{
		get;
	}
}
