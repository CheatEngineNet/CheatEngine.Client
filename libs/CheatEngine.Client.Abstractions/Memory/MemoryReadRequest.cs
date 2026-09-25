using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable typed target-memory read request.</summary>
/// <typeparam name="T">The type of the value the codec reads.</typeparam>
/// <remarks>
///     A <see langword="default" /> request carries no codec: <see cref="IMemoryClient" /> throws an
///     <see cref="ArgumentException" /> for it before the activation check and before any Cheat Engine call.
/// </remarks>
public readonly record struct MemoryReadRequest<T>
{
	/// <summary>Creates a memory read request.</summary>
	/// <param name="address">The target address to read.</param>
	/// <param name="codec">The codec that reads the value.</param>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
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
