using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable typed target-memory write request.</summary>
public readonly record struct MemoryWriteRequest<T>
{
	/// <summary>Creates a memory write request.</summary>
	public MemoryWriteRequest(Address address, T value, IMemoryCodec<T> codec)
	{
		ArgumentNullException.ThrowIfNull(codec);
		Address = address;
		Value = value;
		Codec = codec;
	}

	/// <summary>Gets the target address to write.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the value to write.</summary>
	public T Value
	{
		get;
	}

	/// <summary>Gets the codec used to translate the value.</summary>
	public IMemoryCodec<T> Codec
	{
		get;
	}
}
