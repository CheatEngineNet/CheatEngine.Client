using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable typed target-memory read request.</summary>
/// <remarks>
///     A <see langword="default" /> request carries no codec: <see cref="IMemoryClient" /> throws an
///     <see cref="ArgumentException" /> for it before the activation check and before any Cheat Engine call.
/// </remarks>
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
